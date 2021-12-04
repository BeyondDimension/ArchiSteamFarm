using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using static ArchiSteamFarm.Library.ASFPathHelper;

namespace ArchiSteamFarm.Library;

/// <summary>
/// 
/// </summary>
public static class ASFPathHelper {
	internal static string DirCreateByNotExists(string dirPath) {
		if (!Directory.Exists(dirPath)) {
			_ = Directory.CreateDirectory(dirPath);
		}
		return dirPath;
	}

	private static string? AppDataDirectory_;

	/// <summary>
	/// <list type="bullet">
	///     <item>
	///         AppData\ASF
	///     </item>
	///     <item>
	///         AppData\ASF
	///     </item>
	/// </list>
	/// </summary>
	public static string AppDataDirectory {
		get {
			if (AppDataDirectory_ == null) {
				ArchiSteamFarmLibrary.ThrowInvalidOperationExceptionForMissingInitialization();
			}
			return AppDataDirectory_;
		}
		set => AppDataDirectory_ = value;
	}

	/// <summary>
	/// <list type="bullet">
	///     <item>
	///         Windows：Logs\ASF
	///     </item>
	///     <item>
	///         Windows Desktop Bridge：{CacheDirectory}\Logs\ASF
	///     </item>
	/// </list>
	/// </summary>
	/// <param name="logDirPath"></param>
	/// <returns></returns>
	public static string GetLogDirectory([NotNull] string logDirPath) {
		string path = (logDirPath.EndsWith(Path.DirectorySeparatorChar) ? logDirPath : logDirPath + Path.DirectorySeparatorChar) + "ASF";
		return DirCreateByNotExists(path);
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="logDirectory"></param>
	/// <returns></returns>
	public static string GetNLogArchiveFileName([NotNull] string logDirectory) {
		string path = logDirectory + Path.DirectorySeparatorChar + SharedInfo.ArchivalLogFile;
		return path;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="logDirectory"></param>
	/// <returns></returns>
	public static string GetNLogFileName([NotNull] string logDirectory) {
		string path = logDirectory + Path.DirectorySeparatorChar + SharedInfo.LogFile;
		return path;
	}

	private static string? WebsiteDirectory_;

	/// <summary>
	/// <list type="bullet">
	///     <item>
	///         AppData\ASF\www
	///     </item>
	/// </list>
	/// </summary>
	public static string WebsiteDirectory {
		get {
			if (WebsiteDirectory_ == null) {
				ArchiSteamFarmLibrary.ThrowInvalidOperationExceptionForMissingInitialization();
			}
			return WebsiteDirectory_;
		}
		set => WebsiteDirectory_ = value;
	}

	/// <summary>
	/// 
	/// </summary>
	public const string NLogGeneralLayout = NLog.Logging.GeneralLayout;

	private static string? LogFileDirectory_;

	/// <summary>
	/// 
	/// </summary>
	public static string LogFileDirectory {
		get {
			if (LogFileDirectory_ == null) {
				ArchiSteamFarmLibrary.ThrowInvalidOperationExceptionForMissingInitialization();
			}
			return LogFileDirectory_;
		}
		internal set => LogFileDirectory_ = value;
	}
}

/// <summary>
/// 
/// </summary>
public enum EPathFolder : byte {
	/// <inheritdoc cref="AppDataDirectory"/>
	ASF,

	/// <inheritdoc cref="SharedInfo.ConfigDirectory"/>
	Config,

	/// <inheritdoc cref="SharedInfo.PluginsDirectory"/>
	Plugin,

	/// <inheritdoc cref="WebsiteDirectory"/>
	WWW,

	/// <inheritdoc cref="LogFileDirectory"/>
	Logs,
}
