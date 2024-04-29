﻿using cmonitor.config;
using cmonitor.startup;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace cmonitor.plugins.ntrights
{
    public sealed class NtrightsStartup : IStartup
    {
        public StartupLevel Level => StartupLevel.Top;

        public void AddClient(ServiceCollection serviceCollection, Config config, Assembly[] assemblies)
        {
#if RELEASE
            NtrightsHelper.AddTokenPrivilege();
#endif

        }

        public void AddServer(ServiceCollection serviceCollection, Config config, Assembly[] assemblies)
        {
        }

        public void UseClient(ServiceProvider serviceProvider, Config config, Assembly[] assemblies)
        {
        }

        public void UseServer(ServiceProvider serviceProvider, Config config, Assembly[] assemblies)
        {
        }
    }
}