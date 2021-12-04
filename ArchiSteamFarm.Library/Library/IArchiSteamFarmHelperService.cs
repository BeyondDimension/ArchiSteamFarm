using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MSEXLog = Microsoft.Extensions.Logging;

namespace ArchiSteamFarm.Library;

/// <summary>
/// 
/// </summary>
public interface IArchiSteamFarmHelperService {
	/// <summary>
	/// 
	/// </summary>
	static IArchiSteamFarmHelperService Instance => Ioc.Default.GetRequiredService<IArchiSteamFarmHelperService>();

	/// <summary>
	/// 
	/// </summary>
	int IPCPortValue { get; }

	/// <summary>
	/// 
	/// </summary>
	/// <returns></returns>
	Task Restart();

	/// <summary>
	/// 
	/// </summary>
	MSEXLog.LogLevel MinimumLevel { get; }
}
