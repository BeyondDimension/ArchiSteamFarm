using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using NLog;
using static ArchiSteamFarm.Library.ASFPathHelper;

namespace ArchiSteamFarm.Library;

/// <summary>
/// 
/// </summary>
public static class ArchiSteamFarmLibrary {
	/// <summary>
	/// Initialize ArchiSteamFarmLibrary.
	/// </summary>
	/// <param name="ioc"></param>
	/// <param name="appDataDirectory"></param>
	/// <param name="logFileDirectory"></param>
	public static void Init(IIoc ioc, string appDataDirectory, string logFileDirectory) {
		Ioc.Default = ioc;
		string appDataDirectory_ = AppDataDirectory = DirCreateByNotExists(Path.Combine(appDataDirectory, "ASF"));
		WebsiteDirectory = DirCreateByNotExists(Path.Combine(appDataDirectory, "ASF", "www"));
		LogFileDirectory = logFileDirectory;
		_ = DirCreateByNotExists(SharedInfo._ConfigDirectory = Path.Combine(appDataDirectory_, SharedInfo.ConfigDirectoryName));
		_ = DirCreateByNotExists(SharedInfo._PluginsDirectory = Path.Combine(appDataDirectory_, SharedInfo.PluginsDirectoryName));
		_ = DirCreateByNotExists(SharedInfo._DebugDirectory = Path.Combine(appDataDirectory_, SharedInfo.DebugDirectoryName));
		TryUnPackASFUI();
	}

	/// <summary>
	/// Throws an <see cref="InvalidOperationException"/> when the <see cref="IServiceProvider"/> property is used before initialization.
	/// </summary>
	[DoesNotReturn]
	internal static void ThrowInvalidOperationExceptionForMissingInitialization() => throw new InvalidOperationException("You must call ArchiSteamFarmLibrary.Init(.. before the program starts.");

	private const string Version_FileName = "VERSION.txt";

	private static void TryUnPackASFUI() {
		string dirPath = WebsiteDirectory;
		string versionFilePath = Path.Combine(dirPath, Version_FileName);
		string versionASFUI = SharedInfo.Version.ToString();
		if (File.Exists(versionFilePath)) {
			string version = File.ReadAllText(versionFilePath);
			if (version != versionASFUI) // The version number file does not match, re-extract
			{
				unPackASFUI(true);
			}
		} else {
			unPackASFUI(false); // The version number file does not exist, re-extract it
		}

		void unPackASFUI(bool dirPathExists) {
			if (dirPathExists || Directory.Exists(dirPath)) {
				Directory.Delete(dirPath, true);
			}

			using MemoryStream stream = new(Resource.asf_ui);
			// .NET Framework Install-Package BrotliSharpLib Or Brotli.NET
			using BrotliStream decompress = new(stream, CompressionMode.Decompress);
			UTF8Encoding utf8NoBOM = new(false, true); // https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/EncodingCache.cs
			using TarArchive archive = TarArchive.CreateInputTarArchive(decompress, utf8NoBOM);
			archive.ExtractContents(dirPath);
			File.WriteAllText(versionFilePath, versionASFUI);
		}
	}
}
