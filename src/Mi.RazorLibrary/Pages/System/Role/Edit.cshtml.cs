﻿using Mi.Application.Contracts.System.Models;

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mi.RazorLibrary.Pages.System.Role
{
    public class EditModel : PageModel
    {
        public SysRoleFull Role { get; set; }
    }
}