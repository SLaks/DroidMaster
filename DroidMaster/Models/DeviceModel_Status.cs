using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DroidMaster.Models {
	partial class DeviceModel {
		private static readonly Regex batteryRegex = new Regex(@"  level: (\d+)");
		private static readonly Regex powerSourceRegex = new Regex(@"([A-Za-z][A-Za-z ]+) powered: true");

		///<summary>Refreshes status properties (eg, battery, screen state) from the device.</summary>
		public async Task Refresh() {
			try {
				var output = await Device.ExecuteShellCommand("dumpsys battery && dumpsys input_method").Complete;
				IsScreenOn = output.Contains("mScreenOn=true");

				BatteryLevel = int.Parse(batteryRegex.Match(output).Groups[1].Value);
				PowerSources = string.Join(", ", powerSourceRegex.Matches(output).Cast<Match>().Select(m => m.Groups[1]));

				try {
					IsRooted = (await Device.ExecuteShellCommand("su -c echo BLAH").Complete).Contains("BLAH");
				} catch (FileNotFoundException) {
					IsRooted = false;
				}

				AndroidVersion = await Device.ExecuteShellCommand("getprop ro.build.version.release").Complete;
				Model = Capitalize(await Device.ExecuteShellCommand("getprop ro.product.brand").Complete)
					  + " " + await Device.ExecuteShellCommand("getprop ro.product.model").Complete;
				IsWiFiEnabled = await Device.ExecuteShellCommand("getprop wlan.driver.status").Complete == "ok";
			} catch (Exception ex) {
				Log($"An error occurred while refreshing device status:\r\n{ex.Message}");
			}
		}

		static string Capitalize(string text) { return string.IsNullOrEmpty(text) ? "" : char.ToUpper(text[0]) + text.Substring(1); }

		bool isScreenOn;
		///<summary>Indicates whether the device screen is on.</summary>
		public bool IsScreenOn {
			get { return isScreenOn; }
			private set { isScreenOn = value; OnPropertyChanged(); }
		}

		int batteryLevel;
		///<summary>Gets the battery level, between 0 and 100.</summary>
		public virtual int BatteryLevel {
			get { return batteryLevel; }
			protected set { batteryLevel = value; OnPropertyChanged(); }
		}

		string powerSources;
		///<summary>Gets a comma-separated list of power sources (eg, AC, USB, Wireless), or null if there is the device is not charging.</summary>
		public string PowerSources {
			get { return powerSources; }
			private set { powerSources = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPowered)); }
		}
		///<summary>Indicates whether the device has any active power source.</summary>
		public bool IsPowered => !string.IsNullOrEmpty(PowerSources);

		bool isRooted;
		///<summary>Indicates whether the device is rooted.</summary>
		public bool IsRooted {
			get { return isRooted; }
			private set { isRooted = value; OnPropertyChanged(); }
		}

		string androidVersion;
		///<summary>Gets the version of the Android system installed on the device.</summary>
		public string AndroidVersion {
			get { return androidVersion; }
			private set { androidVersion = value; OnPropertyChanged(); }
		}

		bool isWiFiEnabled;
		///<summary>Indicates whether the Wi-Fi radio is enabled.</summary>
		public bool IsWiFiEnabled {
			get { return isWiFiEnabled; }
			private set { isWiFiEnabled = value; OnPropertyChanged(); }
		}

		string model;
		///<summary>Gets the device brand and model.</summary>
		public string Model {
			get { return model; }
			private set { model = value; OnPropertyChanged(); }
		}
	}
}
