﻿// Copyright (c) 2021 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AuthPermissions.BaseCode.DataLayer.Classes;
using AuthPermissions.BaseCode.DataLayer.EfCode;
using AuthPermissions.BaseCode.PermissionsCode;
using AuthPermissions.BaseCode.SetupCode;
using AuthPermissions.SupportCode.DownStatusCode;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Net.DistributedFileStoreCache;
using Test.StubClasses;
using TestSupport.EfHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions.AssertExtensions;

namespace Test.UnitTests.TestExamplesCommonCode;

public class TestRedirectUsersViaStatusData
{
    private readonly ITestOutputHelper _output;
    private readonly IDistributedFileStoreCacheClass _fsCache;
    //private readonly ISetRemoveStatusService _removeService;

    public TestRedirectUsersViaStatusData(ITestOutputHelper output)
    {
        _output = output;
        _fsCache = new StubFileStoreCacheClass(); //this clears the cache fro each test
        //_removeService = new SetRemoveStatusService(_fsCache, );
    }

    private static AuthPermissionsDbContext SaveTenantToDatabase(Tenant testTenant)
    {
        var options = SqliteInMemory.CreateOptions<AuthPermissionsDbContext>();
        options.StopNextDispose();
        var context = new AuthPermissionsDbContext(options);
        context.Database.EnsureCreated();
        context.Add(testTenant);

        context.SaveChanges();
        return context;
    }

    private ClaimsPrincipal DefaultUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {
            new Claim(ClaimTypes.NameIdentifier, "12345"),
            new Claim(PermissionConstants.DataKeyClaimType, "0.1.2"),
        }, "test"));
    }

    private void AddAllDownCacheValue(string userId = "67890", int? expectedTimeDownMinutes = null)
    {
        var data = new ManuelAppDownDto
        {
            UserId = userId,
            StartedUtc = DateTime.UtcNow,
            Message = "Down",
            ExpectedTimeDownMinutes = expectedTimeDownMinutes
        };
        _fsCache.SetClass(RedirectUsersViaStatusData.DownForStatusAllAppDown, data);
    }

    private RedirectUsersViaStatusData SetupHandler(KeyValuePair<string, string> overrideRoutes = new(),
        TenantTypes tenantTypes = TenantTypes.HierarchicalTenant)
    {
        var routeDict = new RouteValueDictionary();
        if (overrideRoutes.Equals(new KeyValuePair<string, string>()))
        {
            routeDict.Add("controller", "home");
        }
        else
        {
            routeDict.Add(overrideRoutes.Key, overrideRoutes.Value);
        }
        var services = new ServiceCollection();
        services.AddSingleton(_fsCache);

        return new RedirectUsersViaStatusData(new RouteData(routeDict), services.BuildServiceProvider(), tenantTypes);
    }

    [Fact]
    public async Task TestCheckNotDivertedRoutes_NotLoggedInUser()
    {
        //SETUP
        var handler = SetupHandler();
        string redirect = null;
        bool nextCalled = false;

        AddAllDownCacheValue();

        //ATTEMPT
        await handler.RedirectUserOnStatusesAsync(new ClaimsPrincipal(new ClaimsIdentity()),
            x => { redirect = x; },
            () => { nextCalled = true; return Task.CompletedTask; }
        );

        //VERIFY
        redirect.ShouldBeNull();
        nextCalled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("controller", "Status", false)]
    [InlineData("area", "Identity", false)]
    [InlineData("controller", "home", true)]
    public async Task TestCheckNotDivertedRoutes(string key, string value, bool diverted)
    {
        //SETUP
        var handler = SetupHandler(new KeyValuePair<string, string>(key, value));
        string redirect = null;
        bool nextCalled = false;

        AddAllDownCacheValue();

        //ATTEMPT
        await handler.RedirectUserOnStatusesAsync(DefaultUser(),
            x => { redirect = x; },
            () => { nextCalled = true; return Task.CompletedTask; }
            );

        //VERIFY
        _output.WriteLine($"Diverted = {diverted}, redirect = {redirect}, nextCalled = {nextCalled}");
        if (diverted)
        {
            redirect.ShouldEqual("/Status/ShowAllDownStatus");
            nextCalled.ShouldBeFalse();
        }
        else
        {
            redirect.ShouldBeNull();
            nextCalled.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task TestUserThatSetAllDownNotRedirected()
    {
        //SETUP
        var handler = SetupHandler();
        string redirect = null;
        bool nextCalled = false;

        AddAllDownCacheValue("12345");

        //ATTEMPT
        await handler.RedirectUserOnStatusesAsync(DefaultUser(),
            x => { redirect = x; },
            () => { nextCalled = true; return Task.CompletedTask; }
        );

        //VERIFY
        redirect.ShouldBeNull();
        nextCalled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("0.1.2", true)]
    [InlineData("0.1.2.3", true)]
    [InlineData("0.1", false)]
    [InlineData("9.10", false)]
    public async Task TestTenantUserDown(string dataKeyDown, bool diverted)
    {
        //SETUP
        var handler = SetupHandler();
        string redirect = null;
        bool nextCalled = false;

        var combinedKey = dataKeyDown.FormUniqueTenantValue();
        _fsCache.Set(RedirectUsersViaStatusData.DownForStatusTenantManuel + combinedKey, combinedKey);

        //ATTEMPT
        await handler.RedirectUserOnStatusesAsync(DefaultUser(),
            x => { redirect = x; },
            () => { nextCalled = true; return Task.CompletedTask; }
        );

        //VERIFY
        _output.WriteLine($"Diverted = {diverted}, redirect = {redirect}, nextCalled = {nextCalled}");
        if (diverted)
        {
            redirect.ShouldEqual("/Status/ShowTenantManuallyDown");
            nextCalled.ShouldBeFalse();
        }
        else
        {
            redirect.ShouldBeNull();
            nextCalled.ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("0.1.2", true)]
    [InlineData("0.1.2.3", true)]
    [InlineData("0.1", false)]
    [InlineData("9.10", false)]
    public async Task TestTenantUserDeleted(string dataKeyDown, bool diverted)
    {
        //SETUP
        var handler = SetupHandler();
        string redirect = null;
        bool nextCalled = false;

        //await _fsCache.AddTenantDeletedStatusCacheAndWaitAsync(dataKeyDown);
        var combinedKey = dataKeyDown.FormUniqueTenantValue();
        _fsCache.Set(RedirectUsersViaStatusData.DeletedTenantStatus + combinedKey, combinedKey);

        //ATTEMPT
        await handler.RedirectUserOnStatusesAsync(DefaultUser(),
            x => { redirect = x; },
            () => { nextCalled = true; return Task.CompletedTask; }
        );

        //VERIFY
        _output.WriteLine($"Diverted = {diverted}, redirect = {redirect}, nextCalled = {nextCalled}");
        if (diverted)
        {
            redirect.ShouldEqual("/Status/ShowTenantDeleted");
            nextCalled.ShouldBeFalse();
        }
        else
        {
            redirect.ShouldBeNull();
            nextCalled.ShouldBeTrue();
        }
    }

}