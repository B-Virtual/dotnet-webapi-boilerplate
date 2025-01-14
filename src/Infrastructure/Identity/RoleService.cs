using FSH.WebApi.Application.Common.Exceptions;
using FSH.WebApi.Application.Common.Interfaces;
using FSH.WebApi.Application.Identity;
using FSH.WebApi.Application.Identity.RoleClaims;
using FSH.WebApi.Application.Identity.Roles;
using FSH.WebApi.Infrastructure.Auth.Permissions;
using FSH.WebApi.Infrastructure.Common.Extensions;
using FSH.WebApi.Infrastructure.Persistence.Context;
using FSH.WebApi.Shared.Authorization;
using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Security.Claims;

namespace FSH.WebApi.Infrastructure.Identity;

public class RoleService : IRoleService
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<RoleService> _localizer;
    private readonly ICurrentUser _currentUser;
    private readonly IRoleClaimsService _roleClaimService;

    public RoleService(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        IStringLocalizer<RoleService> localizer,
        ICurrentUser currentUser,
        IRoleClaimsService roleClaimService)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
        _localizer = localizer;
        _currentUser = currentUser;
        _roleClaimService = roleClaimService;
    }

    public async Task<string> DeleteAsync(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);

        _ = role ?? throw new NotFoundException(_localizer["Role Not Found"]);

        if (DefaultRoles.Contains(role.Name))
        {
            throw new ConflictException(string.Format(_localizer["Not allowed to delete {0} Role."], role.Name));
        }

        bool roleIsNotUsed = true;
        var allUsers = await _userManager.Users.ToListAsync();
        foreach (var user in allUsers)
        {
            if (await _userManager.IsInRoleAsync(user, role.Name))
            {
                roleIsNotUsed = false;
            }
        }

        if (roleIsNotUsed)
        {
            await _roleManager.DeleteAsync(role);
            return string.Format(_localizer["Role {0} Deleted."], role.Name);
        }
        else
        {
            throw new ConflictException(string.Format(_localizer["Not allowed to delete {0} Role as it is being used."], role.Name));
        }
    }

    public async Task<RoleDto> GetByIdAsync(string id)
    {
        var role = await _roleManager.Roles.SingleOrDefaultAsync(x => x.Id == id);

        _ = role ?? throw new NotFoundException(_localizer["Role Not Found"]);

        var roleDto = role.Adapt<RoleDto>();
        roleDto.IsDefault = DefaultRoles.Contains(role.Name);

        return roleDto;
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken) =>
        await _roleManager.Roles.CountAsync(cancellationToken);

    public async Task<List<RoleDto>> GetListAsync()
    {
        var roles = await _roleManager.Roles.ToListAsync();

        var roleDtos = roles.Adapt<List<RoleDto>>();
        roleDtos.ForEach(role => role.IsDefault = DefaultRoles.Contains(role.Name));

        return roleDtos;
    }

    /// <summary>
    /// Get Permissions By Role Async.
    /// </summary>
    /// <param name="roleId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<RoleDto> GetByIdWithPermissionsAsync(string roleId, CancellationToken cancellationToken)
    {
        var role = await this.GetByIdAsync(roleId);
        var permissions = await _context.RoleClaims.Where(a => a.RoleId == roleId && a.ClaimType == FSHClaims.Permission)
            .ToListAsync(cancellationToken);
        role.Permissions = permissions.Adapt<List<PermissionDto>>();

        return role;
    }

    public async Task<List<RoleDto>> GetUserRolesAsync(string userId)
    {
        var userRoles = await _context.UserRoles.Where(a => a.UserId == userId).Select(a => a.RoleId).ToListAsync();
        var roles = await _roleManager.Roles.Where(a => userRoles.Contains(a.Id)).ToListAsync();

        var roleDtos = roles.Adapt<List<RoleDto>>();
        roleDtos.ForEach(role => role.IsDefault = DefaultRoles.Contains(role.Name));

        return roleDtos;
    }

    public async Task<bool> ExistsAsync(string roleName, string? excludeId) =>
        await _roleManager.FindByNameAsync(roleName)
            is ApplicationRole existingRole
            && existingRole.Id != excludeId;

    public async Task<string> RegisterRoleAsync(RoleRequest request)
    {
        if (string.IsNullOrEmpty(request.Id))
        {
            var newRole = new ApplicationRole(request.Name, request.Description);
            var result = await _roleManager.CreateAsync(newRole);

            return result.Succeeded
                ? string.Format(_localizer["Role {0} Created."], request.Name)
                : throw new InternalServerException(_localizer["Register role failed"], result.Errors.Select(e => _localizer[e.Description].ToString()).ToList());
        }
        else
        {
            var role = await _roleManager.FindByIdAsync(request.Id);

            _ = role ?? throw new NotFoundException(_localizer["Role Not Found"]);

            if (DefaultRoles.Contains(role.Name))
            {
                throw new ConflictException(string.Format(_localizer["Not allowed to modify {0} Role."], role.Name));
            }

            role.Name = request.Name;
            role.NormalizedName = request.Name.ToUpperInvariant();
            role.Description = request.Description;
            var result = await _roleManager.UpdateAsync(role);

            return result.Succeeded
                ? string.Format(_localizer["Role {0} Updated."], role.Name)
                : throw new InternalServerException(_localizer["Update role failed"], result.Errors.Select(e => _localizer[e.Description].ToString()).ToList());
        }
    }

    /// <summary>
    /// Update Permissions by Role Async.
    /// </summary>
    /// <param name="roleId"></param>
    /// <param name="selectedPermissions"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<string> UpdatePermissionsAsync(UpdatePermissionsRequest request, CancellationToken cancellationToken)
    {
        var selectedPermissions = request.Permissions;
        var role = await _roleManager.FindByIdAsync(request.RoleId);
        _ = role ?? throw new NotFoundException(_localizer["Role Not Found"]);

        if (role.Name == FSHRoles.Admin)
        {
            var currentUser = await _userManager.Users.SingleAsync(x => x.Id == _currentUser.GetUserId().ToString());
            if (!await _userManager.IsInRoleAsync(currentUser, FSHRoles.Admin))
            {
                throw new ConflictException(_localizer["Not allowed to modify Permissions for this Role."]);
            }
        }

        if (role.Name == FSHRoles.Admin)
        {
            if (!selectedPermissions.Any(x => x == FSHPermissions.Roles.View)
                || !selectedPermissions.Any(x => x == FSHPermissions.RoleClaims.View)
                || !selectedPermissions.Any(x => x == FSHPermissions.RoleClaims.Edit))
            {
                throw new ConflictException(string.Format(
                    _localizer["Not allowed to deselect {0} or {1} or {2} for this Role."],
                    FSHPermissions.Roles.View,
                    FSHPermissions.RoleClaims.View,
                    FSHPermissions.RoleClaims.Edit));
            }
        }

        var currentPermissions = await _roleManager.GetClaimsAsync(role);

        // Remove permissions that were previously selected
        foreach (var claim in currentPermissions.Where(c => !selectedPermissions.Any(p => p == c.Value)))
        {
            var removeResult = await _roleManager.RemoveClaimAsync(role, claim);
            if (!removeResult.Succeeded)
            {
                throw new InternalServerException(_localizer["Update permissions failed."], removeResult.Errors.Select(e => _localizer[e.Description].ToString()).ToList());
            }
        }

        // Add all permissions that were not previously selected
        foreach (var permission in selectedPermissions.Where(c => !currentPermissions.Any(p => p.Value == c)))
        {
            if (!string.IsNullOrEmpty(permission))
            {
                var addResult = await _roleManager.AddClaimAsync(role, new Claim(FSHClaims.Permission, permission));
                if (!addResult.Succeeded)
                {
                    throw new InternalServerException(_localizer["Update permissions failed."], addResult.Errors.Select(e => _localizer[e.Description].ToString()).ToList());
                }
            }
        }

        return _localizer["Permissions Updated."];
    }

    internal static List<string> DefaultRoles =>
        typeof(FSHRoles).GetAllPublicConstantValues<string>();
}