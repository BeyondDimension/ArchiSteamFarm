//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2022 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if NETFRAMEWORK || USE_ASPNETCORE_2_2
using IHost = Microsoft.AspNetCore.Hosting.IWebHost;
using HostBuilder = Microsoft.AspNetCore.Hosting.WebHostBuilder;
#endif
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.NLog.Targets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog.Web;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting.Server;
#if OUTPUT_TYPE_LIBRARY
using ArchiSteamFarm.Library;
#endif

namespace ArchiSteamFarm.IPC;

internal static class ArchiKestrel {
	internal static bool IsRunning => KestrelWebHost != null;

	internal static HistoryTarget? HistoryTarget { get; private set; }

	private static IHost? KestrelWebHost;

#if OUTPUT_TYPE_LIBRARY
	public static bool IsReady { get; private set; }

	public static ICollection<string>? ServerAddresses {
		get {
			try {
#if NETFRAMEWORK || USE_ASPNETCORE_2_2
				return KestrelWebHost?.ServerFeatures.Get<IServerAddressesFeature>()?.Addresses;
#else
				return KestrelWebHost?.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
#endif

			} catch {
			}
			return null;
		}
	}
#endif

	internal static void OnNewHistoryTarget(HistoryTarget? historyTarget = null) {
		if (HistoryTarget != null) {
			HistoryTarget.NewHistoryEntry -= NLogController.OnNewHistoryEntry;
			HistoryTarget = null;
		}

		if (historyTarget != null) {
			historyTarget.NewHistoryEntry += NLogController.OnNewHistoryEntry;
			HistoryTarget = historyTarget;
		}
	}

	internal static async Task Start() {
		if (KestrelWebHost != null) {
			return;
		}

		ASF.ArchiLogger.LogGenericInfo(Strings.IPCStarting);

		// The order of dependency injection matters, pay attention to it
		HostBuilder builder = new();

#if OUTPUT_TYPE_LIBRARY
		string websiteDirectory = ASFPathHelper.WebsiteDirectory;
#else
		string customDirectory = Path.Combine(Directory.GetCurrentDirectory(), SharedInfo.WebsiteDirectory);
		string websiteDirectory = Directory.Exists(customDirectory) ? customDirectory : Path.Combine(AppContext.BaseDirectory, SharedInfo.WebsiteDirectory);
#endif

		// Set default content root
		builder.UseContentRoot(SharedInfo.HomeDirectory);

		// Check if custom config is available
		string absoluteConfigDirectory = Path.Combine(Directory.GetCurrentDirectory(), SharedInfo.ConfigDirectory);
		string customConfigPath = Path.Combine(absoluteConfigDirectory, SharedInfo.IPCConfigFile);

		bool customConfigExists = File.Exists(customConfigPath);

		if (customConfigExists) {
			if (Debugging.IsDebugConfigured) {
				try {
					string json = await File.ReadAllTextAsync(customConfigPath).ConfigureAwait(false);

					if (!string.IsNullOrEmpty(json)) {
						JObject jObject = JObject.Parse(json);

						ASF.ArchiLogger.LogGenericDebug($"{SharedInfo.IPCConfigFile}: {jObject.ToString(Formatting.Indented)}");
					}
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}
		}

		// Enable NLog integration for logging
		builder.UseNLog();

		builder.ConfigureWebHostDefaults(
			webBuilder => {
				// Set default web root
				if (Directory.Exists(websiteDirectory)) {
					webBuilder.UseWebRoot(websiteDirectory);
				}
				// Now conditionally initialize settings that are not possible to override
				if (customConfigExists) {
					// Set up custom config to be used
					webBuilder.UseConfiguration(new ConfigurationBuilder().SetBasePath(absoluteConfigDirectory).AddJsonFile(SharedInfo.IPCConfigFile, false, Program.ConfigWatch).Build());

					// Use custom config for Kestrel configuration
					webBuilder.UseKestrel(static (builderContext, options) => options.Configure(builderContext.Configuration.GetSection("Kestrel")));
				} else {
					// Use ASF defaults for Kestrel
					webBuilder.UseKestrel(static options => options.ListenLocalhost(
#if OUTPUT_TYPE_LIBRARY
						IArchiSteamFarmHelperService.Instance.IPCPortValue
#else
						1242
#endif
						));
				}

				// Specify Startup class for IPC
				webBuilder.UseStartup<Startup>();
			}
		);

		// Init history logger for /Api/Log usage
		Logging.InitHistoryLogger();

		// Start the server
		IHost? kestrelWebHost = null;

		try {
			kestrelWebHost = builder.Build();
			await kestrelWebHost.StartAsync().ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
			kestrelWebHost?.Dispose();

			return;
		}

		KestrelWebHost = kestrelWebHost;
#if OUTPUT_TYPE_LIBRARY
		IsReady = true;
#endif
		ASF.ArchiLogger.LogGenericInfo(Strings.IPCReady);
	}

	internal static async Task Stop() {
#if OUTPUT_TYPE_LIBRARY
		IsReady = false;
#endif
		if (KestrelWebHost == null) {
			return;
		}

		await KestrelWebHost.StopAsync().ConfigureAwait(false);
		KestrelWebHost.Dispose();
		KestrelWebHost = null;
	}
}
