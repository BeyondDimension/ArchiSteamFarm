//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;
using Newtonsoft.Json;
#if OUTPUT_TYPE_LIBRARY
#if __ANDROID__
using HttpHandlerType = Xamarin.Android.Net.AndroidClientHandler;
#elif __IOS__
using HttpHandlerType = System.Net.Http.NSUrlSessionHandler;
#else
using HttpHandlerType = System.Net.Http.HttpClientHandler;
#endif
using CreateHttpHandlerArgs = System.ValueTuple<
	bool,
	System.Net.DecompressionMethods,
	System.Net.CookieContainer,
	System.Net.IWebProxy?,
	int>;
#if !EMBEDDED_IN_STEAMPLUSPLUS
using System.Diagnostics.CodeAnalysis;
#endif
#endif
#if EMBEDDED_IN_STEAMPLUSPLUS
using static System.Net.Http.GeneralHttpClientFactory;
#endif

namespace ArchiSteamFarm.Web;

public sealed class WebBrowser : IDisposable {
	[PublicAPI]
	public const byte MaxTries = 5; // Defines maximum number of recommended tries for a single request

	internal const byte MaxConnections = 5; // Defines maximum number of connections per ServicePoint. Be careful, as it also defines maximum number of sockets in CLOSE_WAIT state

	private const byte ExtendedTimeoutMultiplier = 10; // Defines multiplier of timeout for WebBrowsers dealing with huge data (ASF update)
	private const byte MaxIdleTime = 15; // Defines in seconds, how long socket is allowed to stay in CLOSE_WAIT state after there are no connections to it

	[PublicAPI]
	public CookieContainer CookieContainer { get; } = new();

	[PublicAPI]
	public TimeSpan Timeout => HttpClient.Timeout;

	private readonly ArchiLogger ArchiLogger;
	private readonly HttpClient HttpClient;
#if OUTPUT_TYPE_LIBRARY
	public HttpMessageHandler HttpClientHandler { get; private set; }
#else
	private readonly HttpClientHandler HttpClientHandler;
#endif

#if OUTPUT_TYPE_LIBRARY

#if !EMBEDDED_IN_STEAMPLUSPLUS
	/// <summary>
	/// https://github.com/dotnet/runtime/blob/v6.0.0/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpNoProxy.cs
	/// </summary>
	private const string HttpNoProxy = "HttpNoProxy";

	private static bool UseWebProxy([NotNullWhen(true)] IWebProxy? proxy)
		=> proxy != null && proxy.GetType().Name != HttpNoProxy;
#endif

	/// <summary>
	/// set this delegate before startup can replace the HttpClientHandler implementation, for example, with WinHttpHandler.
	/// </summary>
	public static Func<CreateHttpHandlerArgs, HttpMessageHandler>? CreateHttpHandlerDelegate { get; set; }

	public static THttpClientHandler CreateHttpHandler<THttpClientHandler>(CreateHttpHandlerArgs args) where THttpClientHandler : HttpHandlerType, new() {
		THttpClientHandler handler = new() {
			AllowAutoRedirect = args.Item1,
#if !__IOS__
			AutomaticDecompression = args.Item2,
#endif
			CookieContainer = args.Item3,
		};
#if !__IOS__
		bool useProxy;
		IWebProxy? proxy;
//#if EMBEDDED_IN_STEAMPLUSPLUS
//		proxy = DefaultProxy;
//		if (UseWebProxy(proxy)) {
//			useProxy = true;
//		} else {
//			proxy = args.Item4;
//			useProxy = UseWebProxy(proxy);
//		}
//#else
		proxy = args.Item4;
		useProxy = UseWebProxy(proxy);
//#endif
		if (useProxy) {
			handler.Proxy = proxy;
			handler.UseProxy = true;
		}
		if (!(args.Item5 < 1)) { // https://github.com/dotnet/runtime/blob/v6.0.0/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/SocketsHttpHandler.cs#L157
			handler.MaxConnectionsPerServer = args.Item5;
		}
#endif
		return handler;
	}

#if NETCOREAPP
	public static SocketsHttpHandler CreateSocketsHttpHandler(CreateHttpHandlerArgs args) {
		SocketsHttpHandler handler = new() {
			AllowAutoRedirect = args.Item1,
			AutomaticDecompression = args.Item2,
			CookieContainer = args.Item3,
		};
		bool useProxy;
		IWebProxy? proxy;
//#if EMBEDDED_IN_STEAMPLUSPLUS
//		proxy = DefaultProxy;
//		if (UseWebProxy(proxy)) {
//			useProxy = true;
//		} else {
//			proxy = args.Item4;
//			useProxy = UseWebProxy(proxy);
//		}
//#else
		proxy = args.Item4;
		useProxy = UseWebProxy(proxy);
//#endif
		if (useProxy) {
			handler.Proxy = proxy;
			handler.UseProxy = true;
		}
		if (!(args.Item5 < 1)) { // https://github.com/dotnet/runtime/blob/v6.0.0/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/SocketsHttpHandler.cs#L157
			handler.MaxConnectionsPerServer = args.Item5;
		}
		return handler;
	}
#endif

	private static HttpMessageHandler CreateHttpHandler(CreateHttpHandlerArgs args) {
		HttpMessageHandler? handler = CreateHttpHandlerDelegate?.Invoke(args);
		if (handler != null) {
			return handler;
		}
#if NETCOREAPP
		// Starting from net 5.0, SocketsHttpHandler and HttpClientHandler use the same implementation on non browser.
		// https://github.com/dotnet/runtime/blob/v5.0.12/src/libraries/System.Net.Http/src/System/Net/Http/HttpClientHandler.cs
		if (SocketsHttpHandler.IsSupported) {
			return CreateSocketsHttpHandler(args);
		}
#endif
		return CreateHttpHandler<HttpHandlerType>(args);
	}
#endif

	internal WebBrowser(ArchiLogger archiLogger, IWebProxy? webProxy = null, bool extendedTimeout = false) {
		ArchiLogger = archiLogger ?? throw new ArgumentNullException(nameof(archiLogger));

#if OUTPUT_TYPE_LIBRARY

		const bool allowAutoRedirect = false; // This must be false if we want to handle custom redirection schemes such as "steammobile://"
		const DecompressionMethods automaticDecompression =
#if NETFRAMEWORK
			DecompressionMethods.Deflate | DecompressionMethods.GZip;
#else
			DecompressionMethods.All;
#endif
		int maxConnectionsPerServer =
#if NET6_0_OR_GREATER
			MaxConnections;
#else
			RuntimeMadness.IsRunningOnMono ? 0 : MaxConnections;
#endif

		CreateHttpHandlerArgs args = (allowAutoRedirect, automaticDecompression,
				CookieContainer,
				webProxy,
				maxConnectionsPerServer);

		HttpClientHandler = CreateHttpHandler(args);

#else

		HttpClientHandler = new HttpClientHandler {
			AllowAutoRedirect = false, // This must be false if we want to handle custom redirection schemes such as "steammobile://"

#if NETFRAMEWORK
			AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
#else
			AutomaticDecompression = DecompressionMethods.All,
#endif

			CookieContainer = CookieContainer
		};

		if (webProxy != null) {
			HttpClientHandler.Proxy = webProxy;
			HttpClientHandler.UseProxy = true;
		}

#if NETFRAMEWORK
		if (!RuntimeMadness.IsRunningOnMono) {
			HttpClientHandler.MaxConnectionsPerServer = MaxConnections;
		}
#else
		HttpClientHandler.MaxConnectionsPerServer = MaxConnections;
#endif

#endif

		HttpClient = GenerateDisposableHttpClient(extendedTimeout);
	}

	public void Dispose() {
		HttpClient.Dispose();
		HttpClientHandler.Dispose();
	}

	[PublicAPI]
	public HttpClient GenerateDisposableHttpClient(bool extendedTimeout = false) {
		if (ASF.GlobalConfig == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalConfig));
		}

		HttpClient result = new(HttpClientHandler, false) {
#if !NETFRAMEWORK
			DefaultRequestVersion = HttpVersion.Version30,
#endif
			Timeout = TimeSpan.FromSeconds(extendedTimeout ? ExtendedTimeoutMultiplier * ASF.GlobalConfig.ConnectionTimeout : ASF.GlobalConfig.ConnectionTimeout)
		};

		// Most web services expect that UserAgent is set, so we declare it globally
		// If you by any chance came here with a very "clever" idea of hiding your ass by changing default ASF user-agent then here is a very good advice from me: don't, for your own safety - you've been warned
		result.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(SharedInfo.PublicIdentifier, SharedInfo.Version.ToString()));
		result.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"({SharedInfo.BuildInfo.Variant}; {OS.Version.Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal)}; +{SharedInfo.ProjectURL})"));

		return result;
	}

	[PublicAPI]
	public async Task<BinaryResponse?> UrlGetToBinary(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, IProgress<byte>? progressReporter = null) {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		for (byte i = 0; i < maxTries; i++) {
			StreamResponse? response = await UrlGetToStream(request, headers, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						// We're not handling this error, do not try again
						break;
					}
				} else if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						// We're not handling this error, try again
						continue;
					}
				}

				progressReporter?.Report(0);

#pragma warning disable CA2000 // False positive, we're actually wrapping it in the using clause below exactly for that purpose
				MemoryStream ms = new((int) response.Length);
#pragma warning restore CA2000 // False positive, we're actually wrapping it in the using clause below exactly for that purpose

				await using (ms.ConfigureAwait(false)) {
					byte batch = 0;
					long readThisBatch = 0;
					long batchIncreaseSize = response.Length / 100;

					ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;

					// This is HttpClient's buffer, using more doesn't make sense
					byte[] buffer = bytePool.Rent(8192);

					try {
						while (response.Content.CanRead) {
							int read = await response.Content.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);

							if (read == 0) {
								break;
							}

							await ms.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);

							if ((progressReporter == null) || (batchIncreaseSize == 0) || (batch >= 99)) {
								continue;
							}

							readThisBatch += read;

							while ((readThisBatch >= batchIncreaseSize) && (batch < 99)) {
								readThisBatch -= batchIncreaseSize;
								progressReporter.Report(++batch);
							}
						}
					} catch (Exception e) {
						ArchiLogger.LogGenericWarningException(e);
						ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

						return null;
					} finally {
						bytePool.Return(buffer);
					}

					progressReporter?.Report(100);

					return new BinaryResponse(response, ms.ToArray());
				}
			}
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return null;
	}

	[PublicAPI]
	public async Task<HtmlDocumentResponse?> UrlGetToHtmlDocument(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		for (byte i = 0; i < maxTries; i++) {
			StreamResponse? response = await UrlGetToStream(request, headers, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						// We're not handling this error, do not try again
						break;
					}
				} else if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						// We're not handling this error, try again
						continue;
					}
				}

				try {
					return await HtmlDocumentResponse.Create(response).ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
				}
			}
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return null;
	}

	[PublicAPI]
	public async Task<ObjectResponse<T>?> UrlGetToJsonObject<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		for (byte i = 0; i < maxTries; i++) {
			StreamResponse? response = await UrlGetToStream(request, headers, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						// We're not handling this error, do not try again
						break;
					}
				} else if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						// We're not handling this error, try again
						continue;
					}
				}

				T? obj;

				try {
					using StreamReader streamReader = new(response.Content);
					using JsonTextReader jsonReader = new(streamReader);

					JsonSerializer serializer = new();

					obj = serializer.Deserialize<T>(jsonReader);

					if (obj is null) {
						ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(obj)));

						continue;
					}
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					continue;
				}

				return new ObjectResponse<T>(response, obj);
			}
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return null;
	}

	[PublicAPI]
	public async Task<StreamResponse?> UrlGetToStream(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		for (byte i = 0; i < maxTries; i++) {
			HttpResponseMessage? response = await InternalGet(request, headers, referer, requestOptions, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
					// We're not handling this error, do not try again
					break;
				}
			} else if (response.StatusCode.IsServerErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
					// We're not handling this error, try again
					continue;
				}
			}

			return new StreamResponse(response, await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return null;
	}

	[PublicAPI]
	public async Task<BasicResponse?> UrlHead(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		BasicResponse? result = null;

		for (byte i = 0; i < maxTries; i++) {
			using HttpResponseMessage? response = await InternalHead(request, headers, referer, requestOptions).ConfigureAwait(false);

			if (response == null) {
				continue;
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
					result = new BasicResponse(response);
				}

				break;
			}

			if (response.StatusCode.IsServerErrorCode()) {
				if (requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
					result = new BasicResponse(response);
				}

				continue;
			}

			return new BasicResponse(response);
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return result;
	}

	[PublicAPI]
	public async Task<BasicResponse?> UrlPost<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		BasicResponse? result = null;

		for (byte i = 0; i < maxTries; i++) {
			using HttpResponseMessage? response = await InternalPost(request, headers, data, referer, requestOptions).ConfigureAwait(false);

			if (response == null) {
				continue;
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
					result = new BasicResponse(response);
				}

				break;
			}

			if (response.StatusCode.IsServerErrorCode()) {
				if (requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
					result = new BasicResponse(response);
				}

				continue;
			}

			return new BasicResponse(response);
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return result;
	}

	[PublicAPI]
	public async Task<HtmlDocumentResponse?> UrlPostToHtmlDocument<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		for (byte i = 0; i < maxTries; i++) {
			StreamResponse? response = await UrlPostToStream(request, headers, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						// We're not handling this error, do not try again
						break;
					}
				} else if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						// We're not handling this error, try again
						continue;
					}
				}

				try {
					return await HtmlDocumentResponse.Create(response).ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
				}
			}
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return null;
	}

	[PublicAPI]
	public async Task<ObjectResponse<TResult>?> UrlPostToJsonObject<TResult, TData>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, TData? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where TData : class {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		for (byte i = 0; i < maxTries; i++) {
			StreamResponse? response = await UrlPostToStream(request, headers, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						// We're not handling this error, do not try again
						break;
					}
				} else if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						// We're not handling this error, try again
						continue;
					}
				}

				TResult? obj;

				try {
					using StreamReader steamReader = new(response.Content);
					using JsonReader jsonReader = new JsonTextReader(steamReader);

					JsonSerializer serializer = new();

					obj = serializer.Deserialize<TResult>(jsonReader);

					if (obj is null) {
						ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(obj)));

						continue;
					}
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					continue;
				}

				return new ObjectResponse<TResult>(response, obj);
			}
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return null;
	}

	[PublicAPI]
	public async Task<StreamResponse?> UrlPostToStream<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (maxTries == 0) {
			throw new ArgumentOutOfRangeException(nameof(maxTries));
		}

		for (byte i = 0; i < maxTries; i++) {
			HttpResponseMessage? response = await InternalPost(request, headers, data, referer, requestOptions, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
					// We're not handling this error, do not try again
					break;
				}
			} else if (response.StatusCode.IsServerErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
					// We're not handling this error, try again
					continue;
				}
			}

			return new StreamResponse(response, await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
		}

		if (maxTries > 1) {
			ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
			ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
		}

		return null;
	}

	internal static void Init() {
		// Set max connection limit from default of 2 to desired value
		ServicePointManager.DefaultConnectionLimit = MaxConnections;

		// Set max idle time from default of 100 seconds (100 * 1000) to desired value
		ServicePointManager.MaxServicePointIdleTime = MaxIdleTime * 1000;

		// Don't use Expect100Continue, we're sure about our POSTs, save some TCP packets
		ServicePointManager.Expect100Continue = false;

		// Reuse ports if possible
#if NETFRAMEWORK
		if (!RuntimeMadness.IsRunningOnMono) {
			ServicePointManager.ReusePort = true;
			// Xamarin.Android incompatible
			// Common7\IDE\ReferenceAssemblies\Microsoft\Framework\MonoAndroid\v1.0\System.dll
			// [MonoTODO]
			// public static bool ReusePort
			// get return false;
			// set throw new NotImplementedException();
		}
#else
		ServicePointManager.ReusePort = true;
#endif
	}

	private async Task<HttpResponseMessage?> InternalGet(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead) {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		return await InternalRequest<object>(request, HttpMethod.Get, headers, null, referer, requestOptions, httpCompletionOption).ConfigureAwait(false);
	}

	private async Task<HttpResponseMessage?> InternalHead(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead) {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		return await InternalRequest<object>(request, HttpMethod.Head, headers, null, referer, requestOptions, httpCompletionOption).ConfigureAwait(false);
	}

	private async Task<HttpResponseMessage?> InternalPost<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead) where T : class {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		return await InternalRequest(request, HttpMethod.Post, headers, data, referer, requestOptions, httpCompletionOption).ConfigureAwait(false);
	}

	private async Task<HttpResponseMessage?> InternalRequest<T>(Uri request, HttpMethod httpMethod, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, byte maxRedirections = MaxTries) where T : class {
		if (request == null) {
			throw new ArgumentNullException(nameof(request));
		}

		if (httpMethod == null) {
			throw new ArgumentNullException(nameof(httpMethod));
		}

		HttpResponseMessage response;

		while (true) {
			using (HttpRequestMessage requestMessage = new(httpMethod, request)) {
#if !NETFRAMEWORK
				requestMessage.Version = HttpClient.DefaultRequestVersion;
#endif

				if (headers != null) {
					foreach ((string header, string value) in headers) {
						requestMessage.Headers.Add(header, value);
					}
				}

				if (data != null) {
					switch (data) {
						case HttpContent content:
							requestMessage.Content = content;

							break;
						case IReadOnlyCollection<KeyValuePair<string?, string?>> nameValueCollection:
							try {
								requestMessage.Content = new FormUrlEncodedContent(nameValueCollection);
							} catch (UriFormatException) {
								requestMessage.Content = new StringContent(string.Join("&", nameValueCollection.Select(static kv => $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value)}")), null, "application/x-www-form-urlencoded");
							}

							break;
						case string text:
							requestMessage.Content = new StringContent(text);

							break;
						default:
							requestMessage.Content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

							break;
					}
				}

				if (referer != null) {
					requestMessage.Headers.Referrer = referer;
				}

				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebug($"{httpMethod} {request}");
				}

				try {
					response = await HttpClient.SendAsync(requestMessage, httpCompletionOption).ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericDebuggingException(e);

					return null;
				} finally {
					if (data is HttpContent) {
						// We reset the request content to null, as our http content will get disposed otherwise, and we still need it for subsequent calls, such as redirections or retries
						requestMessage.Content = null;
					}
				}
			}

			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug($"{response.StatusCode} <- {httpMethod} {request}");
			}

			if (response.IsSuccessStatusCode) {
				return response;
			}

			// WARNING: We still have not disposed response by now, make sure to dispose it ASAP if we're not returning it!
			if (response.StatusCode is >= HttpStatusCode.Ambiguous and < HttpStatusCode.BadRequest && (maxRedirections > 0)) {
				Uri? redirectUri = response.Headers.Location;

				if (redirectUri == null) {
					ArchiLogger.LogNullError(nameof(redirectUri));

					return null;
				}

				if (redirectUri.IsAbsoluteUri) {
					switch (redirectUri.Scheme) {
						case "http":
						case "https":
							break;
						case "steammobile":
							// Those redirections are invalid, but we're aware of that and we have extra logic for them
							return response;
						default:
							// We have no clue about those, but maybe HttpClient can handle them for us
							ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(redirectUri.Scheme), redirectUri.Scheme));

							break;
					}
				} else {
					redirectUri = new Uri(request, redirectUri);
				}

				switch (response.StatusCode) {
					case HttpStatusCode.MovedPermanently: // Per https://tools.ietf.org/html/rfc7231#section-6.4.2, a 301 redirect may be performed using a GET request
					case HttpStatusCode.Redirect: // Per https://tools.ietf.org/html/rfc7231#section-6.4.3, a 302 redirect may be performed using a GET request
					case HttpStatusCode.SeeOther: // Per https://tools.ietf.org/html/rfc7231#section-6.4.4, a 303 redirect should be performed using a GET request
						if (httpMethod != HttpMethod.Head) {
							httpMethod = HttpMethod.Get;
						}

						// Data doesn't make any sense for a fetch request, clear it in case it's being used
						data = null;

						break;
				}

				response.Dispose();

				// Per https://tools.ietf.org/html/rfc7231#section-7.1.2, a redirect location without a fragment should inherit the fragment from the original URI
				if (!string.IsNullOrEmpty(request.Fragment) && string.IsNullOrEmpty(redirectUri.Fragment)) {
					redirectUri = new UriBuilder(redirectUri) { Fragment = request.Fragment }.Uri;
				}

				request = redirectUri;
				maxRedirections--;

				continue;
			}

			break;
		}

		if (!Debugging.IsUserDebugging) {
			ArchiLogger.LogGenericDebug($"{response.StatusCode} <- {httpMethod} {request}");
		}

		if (response.StatusCode.IsClientErrorCode()) {
			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.Content, await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
			}

			// Do not retry on client errors
			return response;
		}

		if (requestOptions.HasFlag(ERequestOptions.ReturnServerErrors) && response.StatusCode.IsServerErrorCode()) {
			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.Content, await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
			}

			// Do not retry on server errors in this case
			return response;
		}

		using (response) {
			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.Content, await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
			}

			return null;
		}
	}

	[Flags]
	public enum ERequestOptions : byte {
		None = 0,
		ReturnClientErrors = 1,
		ReturnServerErrors = 2
	}
}
