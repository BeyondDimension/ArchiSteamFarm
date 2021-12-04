using System;
using System.Collections.Generic;
using System.Text;

namespace ArchiSteamFarm.Library;

internal static class Ioc {
	private static IIoc? Default_;

	public static IIoc Default {
		get {
			if (Default_ is null) {
				ArchiSteamFarmLibrary.ThrowInvalidOperationExceptionForMissingInitialization();
			}
			return Default_;
		}
		set => Default_ = value;
	}
}

/// <summary>
/// A type that facilitates the use of the <see cref="IServiceProvider"/> type.
/// The <see cref="IIoc"/> provides the ability to configure services in a singleton, thread-safe
/// service provider instance, which can then be used to resolve service instances.
/// The first step to use this feature is to declare some services, for instance:
/// </summary>
public interface IIoc {
	/// <summary>
	/// Tries to resolve an instance of a specified service type.
	/// </summary>
	/// <typeparam name="T">The type of service to resolve.</typeparam>
	/// <returns>An instance of the specified service, or <see langword="null"/>.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the current <see cref="IIoc"/> instance has not been initialized.</exception>
	T? GetService<T>() where T : class;

	/// <summary>
	/// Resolves an instance of a specified service type.
	/// </summary>
	/// <typeparam name="T">The type of service to resolve.</typeparam>
	/// <returns>An instance of the specified service, or <see langword="null"/>.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the current <see cref="Ioc"/> instance has not been initialized, or if the
	/// requested service type was not registered in the service provider currently in use.
	/// </exception>
	T GetRequiredService<T>() where T : class;
}
