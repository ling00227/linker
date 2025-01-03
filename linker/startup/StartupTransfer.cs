﻿using linker.config;
using linker.libs;
using Microsoft.Extensions.DependencyInjection;

namespace linker.startup
{
    public static partial class StartupTransfer
    {
        static List<IStartup> startups = new List<IStartup>();
        /// <summary>
        /// 反射读取所有插件
        /// </summary>
        /// <param name="config"></param>
        /// <param name="assemblies"></param>
        public static void Init(FileConfig config)
        {
            List<IStartup> temps = GetSourceGeneratorInstances().OrderByDescending(c => c.Level).ToList();
            TestDependent(temps);
            LoadPlugins(config, temps); 
        }
        /// <summary>
        /// 检查插件依赖
        /// </summary>
        /// <param name="temps"></param>
        private static void TestDependent(List<IStartup> temps)
        {
            IEnumerable<string> names = temps.Select(c => c.Name);
            foreach (IStartup item in temps.Where(c => c.Dependent.Length > 0))
            {
                IEnumerable<string> excepts = item.Dependent.Except(names);
                if (excepts.Any())
                {
                    LoggerHelper.Instance.Error($"【{item.Name}】dependent by {string.Join(",", excepts)}，but it not exists");
                }
            }
        }
        /// <summary>
        /// 加载插件
        /// </summary>
        /// <param name="config"></param>
        /// <param name="temps"></param>
        private static void LoadPlugins(FileConfig config, List<IStartup> temps)
        {
            //只要哪些
            if (config.Data.Common.IncludePlugins.Length > 0)
            {
                temps = temps.Where(c => c.Required || config.Data.Common.IncludePlugins.Contains(c.Name)).ToList();
            }
            //不要哪些
            else if (config.Data.Common.ExcludePlugins.Length > 0)
            {
                temps = temps.Where(c => c.Required || config.Data.Common.ExcludePlugins.Contains(c.Name) == false).ToList();
            }

            LoadDependents(temps, temps.Select(c => c.Name));
            startups = startups.Distinct().ToList();

            config.Data.Common.Plugins = startups.Select(c => c.Name).ToArray();

            LoggerHelper.Instance.Info($"load startup : {string.Join(",", startups.Select(c => c.GetType().Name))}");
        }
        /// <summary>
        /// 加载插件依赖
        /// </summary>
        /// <param name="all"></param>
        /// <param name="sependents"></param>
        private static void LoadDependents(List<IStartup> all, IEnumerable<string> sependents)
        {
            if (sependents.Any() == false) return;

            IEnumerable<IStartup> temps = all.Where(c => sependents.Contains(c.Name));
            IEnumerable<string> _sependents = temps.SelectMany(c => c.Dependent);

            startups.AddRange(temps);

            LoadDependents(all, _sependents);
        }

        /// <summary>
        /// 注入
        /// </summary>
        /// <param name="serviceCollection"></param>
        /// <param name="config"></param>
        /// <param name="assemblies"></param>
        public static void Add(ServiceCollection serviceCollection, FileConfig config)
        {
            LoggerHelper.Instance.Info($"add startup : {string.Join(",", startups.Select(c => c.GetType().Name))}");
            foreach (var startup in startups)
            {
                if (config.Data.Common.Modes.Contains("client"))
                {
                    startup.AddClient(serviceCollection, config);
                }
                if (config.Data.Common.Modes.Contains("server"))
                {
                    startup.AddServer(serviceCollection, config);
                }
            }
        }

        /// <summary>
        /// 启动
        /// </summary>
        /// <param name="serviceProvider"></param>
        /// <param name="config"></param>
        /// <param name="assemblies"></param>
        public static void Use(ServiceProvider serviceProvider, FileConfig config)
        {
            LoggerHelper.Instance.Info($"use startup : {string.Join(",", startups.Select(c => c.GetType().Name))}");
            foreach (var startup in startups)
            {
                if (config.Data.Common.Modes.Contains("client"))
                {
                    startup.UseClient(serviceProvider, config);
                }
                if (config.Data.Common.Modes.Contains("server"))
                {
                    startup.UseServer(serviceProvider, config);
                }
            }
        }
    }
}
