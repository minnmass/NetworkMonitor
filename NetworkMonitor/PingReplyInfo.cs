using System;
using System.Net.NetworkInformation;

namespace NetworkMonitor {
	public class PingReplyInfo {
		public PingReply Reply { get; set; }
		public String ErrorMessage { get; set; }
	}
}
