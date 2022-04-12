using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace NetworkMonitor {
	class Program {
		private const string _externalHost = "www.google.com";
		//private const string _externalHost = "192.168.4.1";
		//private const string _externalHost = "asdfas";
		private const string _outputPath = "out.log";
		private const int _pingTimeoutMS = 1000;
		private const int _pingFrequencyMS = 100;
		private const int _pingThresholdMs = 120;

		// Create a buffer of 32 bytes of data to be transmitted.
		private static readonly byte[] _pingPayload;
		private static readonly PingOptions _pingOptions = new() { DontFragment = true };

		private static IPAddress _gatewayAddress;
		private static readonly ConcurrentQueue<PingPair> _queue = new();

		static Program() {
			_pingPayload = Encoding.ASCII.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
		}

		public static async Task Main(string[] args) {
			IPHostEntry entry;
			try {
				entry = Dns.GetHostEntry(_externalHost);
			} catch {
				Console.WriteLine($"Could not resolve {_externalHost}.");
				Console.WriteLine("Check your Internet connection.");
				return;
			}

			if (entry.AddressList.Length == 0) {
				Console.WriteLine($"Could not resolve {_externalHost}.");
				Console.WriteLine("Check your Internet connection.");
				return;
			}

			try {
				_gatewayAddress = GatewayHelper.GetGatewayForDestination(entry.AddressList[0]);
				if (_gatewayAddress is null) {
					Console.WriteLine($"Could not find gateway for {_externalHost}. Very odd.");
					Console.WriteLine("Double-check your Internet connection.");
					return;
				}
			} catch (Exception ex) {
				Console.WriteLine(ex.Message);
				return;
			}

			_ = WriteTask();

			try {
				while (true) {
					var pair = SendPings();
					var delay = Task.Delay(_pingFrequencyMS);

					_queue.Enqueue(await pair);

					await delay;
				}
			} catch (Exception ex) {
				Console.WriteLine(ex);
				Console.WriteLine(ex.InnerException?.Message);
			}
		}

		private static async Task WriteTask() {
			string externalError = $"Error pinging {_externalHost}:";
			string externalPingTimeError = $"Excessive RTT pinging {_externalHost}: ";
			string gatewayError = $"Error pinging internal gateway:";
			int delayMS = _pingFrequencyMS * 100;

			StringBuilder errorMessage = new();
			while (true) {
				while (_queue.TryDequeue(out var pair)) {
					errorMessage.Clear();
					var error = Error(pair);
					if (!String.IsNullOrWhiteSpace(error)) {
						await File.AppendAllTextAsync(_outputPath, error);
						Console.WriteLine(error);
					}
				}
				await Task.Delay(delayMS);
			}

			string Error(PingPair pair) {
				bool isError = false;
				if (pair.External.ErrorMessage != null) {
					isError = true;
					errorMessage.AppendLine(externalError);
					errorMessage.AppendLine(pair.External.ErrorMessage);
				} else if (pair.External.Reply?.Status != IPStatus.Success) {
					isError = true;
					errorMessage.AppendLine(externalError);
					errorMessage.AppendLine(pair.External.Reply?.Status.ToString() ?? "unknown error");
				} else if (pair.External.Reply.RoundtripTime > _pingThresholdMs) {
					isError = true;
					errorMessage.Append(externalPingTimeError);
					errorMessage.Append(pair.External.Reply.RoundtripTime.ToString());
					errorMessage.AppendLine("ms");
				}
				if (isError) {
					if (pair.Gateway.ErrorMessage != null) {
						errorMessage.AppendLine(gatewayError);
						errorMessage.AppendLine(pair.Gateway.ErrorMessage);
					} else if (pair.Gateway.Reply?.Status != IPStatus.Success) {
						errorMessage.Append(gatewayError);
						errorMessage.AppendLine(pair.Gateway.Reply?.Status.ToString() ?? "unknown error");
					} else {
						errorMessage.Append("Gateway responded to ping in ");
						errorMessage.Append(pair.Gateway.Reply.RoundtripTime.ToString());
						errorMessage.AppendLine("ms");
					}

					return $"{pair.Start:O}{Environment.NewLine}{errorMessage}{Environment.NewLine}{Environment.NewLine}";
				}
				return null;
			}
		}

		private static async Task<PingPair> SendPings() {
			var pair = new PingPair();

			var external = SendExternalPing();
			var gateway = SendGatewayPing();

			await Task.WhenAll(external, gateway);

			pair.External = external.Result;
			pair.Gateway = gateway.Result;

			return pair;
		}

		private static async Task<PingReplyInfo> SendExternalPing() {
			PingReplyInfo info = new();
			try {
				using (var ping = new Ping()) {
					info.Reply = await ping.SendPingAsync(_externalHost, _pingTimeoutMS, _pingPayload, _pingOptions);
				}
			} catch (Exception ex) {
				info.ErrorMessage = ex.Message;
			}
			return info;
		}

		private static async Task<PingReplyInfo> SendGatewayPing() {
			PingReplyInfo info = new();
			try {
				using (var ping = new Ping()) {
					info.Reply = await ping.SendPingAsync(_gatewayAddress, _pingTimeoutMS, _pingPayload, _pingOptions);
				}
			} catch (Exception ex) {
				info.ErrorMessage = ex.Message;
			}
			return info;
		}

		private class PingPair {
			public DateTime Start { get; } = DateTime.UtcNow;
			public PingReplyInfo External { get; set; }
			public PingReplyInfo Gateway { get; set; }
		}
	}
}
