﻿using System.Linq.Expressions;

using Mi.Domain.DataAccess;
using Mi.Domain.Entities;
using Mi.Domain.Helper;
using Mi.Domain.Shared.Core;
using Mi.Domain.Shared.Models;

using Microsoft.EntityFrameworkCore;

using Nito.AsyncEx;

namespace Mi.DataDriver.EntityFrameworkCore
{
    internal class Repository<T> : IRepository<T> where T : EntityBase, new()
    {
        private readonly MiDbContext _dbContext;
        private readonly ICurrentUser _currentUser;
        private readonly AsyncLock _mutex = new AsyncLock();

        public Repository(MiDbContext dbContext, ICurrentUser currentUser)
        {
            _dbContext = dbContext;
            _currentUser = currentUser;
        }

        public async Task<int> AddAsync(T model)
        {
            WithCreatedFields(model);

            _dbContext.Add(model);
            return await _dbContext.SaveChangesAsync();
        }

        public async Task<int> AddRangeAsync(IEnumerable<T> models)
        {
            foreach (var model in models)
            {
                WithCreatedFields(model);
            }

            await _dbContext.AddRangeAsync(models);
            return await _dbContext.SaveChangesAsync();
        }

        public Task<bool> AnyAsync(Expression<Func<T, bool>>? expression = null)
        {
            expression ??= x => x.IsDeleted == 0;
            return _dbContext.Set<T>().AnyAsync(expression);
        }

        public Task<int> CountAsync(Expression<Func<T, bool>>? expression = null)
        {
            expression ??= x => x.IsDeleted == 0;
            return _dbContext.Set<T>().CountAsync(expression);
        }

        public async Task<int> DeleteAsync(long id)
        {
            var model = await GetAsync(x => x.Id == id);
            if (model == null) return 0;
            _dbContext.Remove(model);
            return await _dbContext.SaveChangesAsync();
        }

        public Task<int> DeleteAsync(T model)
        {
            _dbContext.Remove(model);
            return _dbContext.SaveChangesAsync();
        }

        public Task<int> DeleteRangeAsync(IEnumerable<T> models)
        {
            _dbContext.RemoveRange(models);
            return _dbContext.SaveChangesAsync();
        }

        public Task<T?> GetAsync(Expression<Func<T, bool>>? expression = null)
        {
            expression ??= x => x.IsDeleted == 0;
            return _dbContext.Set<T>().FirstOrDefaultAsync(expression);
        }

        public async Task<List<T>> GetListAsync(Expression<Func<T, bool>>? expression = null)
        {
            expression ??= x => x.IsDeleted == 0;
            return await _dbContext.Set<T>().Where(expression).ToListAsync();
        }

        public Task<int> UpdateAsync(T model)
        {
            WithModifiedFields(model);

            _dbContext.Update(model);
            return _dbContext.SaveChangesAsync();
        }

        public Task<int> UpdateRangeAsync(IEnumerable<T> models)
        {
            foreach (var model in models)
            {
                WithModifiedFields(model);
            }

            _dbContext.UpdateRange(models);
            return _dbContext.SaveChangesAsync();
        }

        private void WithModifiedFields(T model)
        {
            if (model.ModifiedBy.GetValueOrDefault() == 0)
                model.ModifiedBy = _currentUser.UserId;
            if (!model.ModifiedOn.HasValue)
                model.ModifiedOn = DateTime.Now;
        }

        private void WithCreatedFields(T model)
        {
            if (model.CreatedBy == 0)
                model.CreatedBy = _currentUser.UserId;
            if (model.CreatedOn.Equals(new DateTime()))
                model.CreatedOn = DateTime.Now;
            if (model.Id == 0)
                model.Id = SnowflakeIdHelper.NextId();
        }

        public async Task<PagingModel<T>> GetPagedAsync(Expression<Func<T, bool>> expression, int page, int size, IEnumerable<QuerySortField>? querySortFields = null)
        {
            var model = new PagingModel<T>();
            model.Total = await _dbContext.Set<T>().CountAsync(expression);
            model.Rows = _dbContext.Set<T>().Where(expression).Skip((page - 1) * size).Take(size).ToList();

            return model;
        }

        public async Task<int> UpdateAsync(long id, Func<Updatable<T>, Updatable<T>> updatable)
        {
            using (await _mutex.LockAsync())
            {
                var updator = Activator.CreateInstance<Updatable<T>>();
                updator = updatable(updator);

                var model = await _dbContext.Set<T>().AsNoTracking().FirstAsync(x => x.Id == id);
                if (model == null) return 0;

                Type type = typeof(T);
                var props = type.GetProperties();
                foreach (var keyValue in updator.KeyValuePairs)
                {
                    System.Reflection.FieldInfo? fieldInfo = type.GetField(keyValue.Key);
                    if (fieldInfo == null) continue;
                    fieldInfo.SetValue(model, keyValue.Value);
                }

                return await UpdateAsync(model);
            }
        }
    }
}