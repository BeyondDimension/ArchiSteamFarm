using ArchiSteamFarm;
using ArchiSteamFarm.Library;
using static ArchiSteamFarm.Library.ASFPathHelper;

/// <summary>
/// 
/// </summary>
#pragma warning disable CA1050 // Declaring types in namespaces
public static class EPathFolderExtensions {
#pragma warning restore CA1050 // Declaring types in namespaces
	/// <summary>
	/// 
	/// </summary>
	/// <param name="folderASFPath"></param>
	/// <returns></returns>
	public static string GetFolderPath(this EPathFolder folderASFPath) {
		string folderASFPathValue = folderASFPath switch {
			EPathFolder.Config => DirCreateByNotExists(SharedInfo.ConfigDirectory),
			EPathFolder.Plugin => DirCreateByNotExists(SharedInfo.PluginsDirectory),
			EPathFolder.WWW => WebsiteDirectory,
			EPathFolder.Logs => LogFileDirectory,
			EPathFolder.ASF or _ => AppDataDirectory,
		};
		return folderASFPathValue;
	}
}
