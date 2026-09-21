/*
	KeeUnicodeURL - KeePass plugin
	IdnUrlUtil.cs

	Conversion of the host part of a URL between Punycode (ACE) and
	Unicode. Uses the self-contained Punycode.cs (RFC 3492 only) rather
	than System.Globalization.IdnMapping - IdnMapping additionally
	enforces IDNA registration-policy rules on top of plain Punycode and
	throws ArgumentException for a number of real, registered,
	resolvable domain names as a result (a documented .NET behaviour,
	not specific to this plugin; see Punycode.cs for details).
*/

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace KeeUnicodeURL
{
	public static class IdnUrlUtil
	{
		private static readonly Regex UrlWithSchemeRx = new Regex(
			@"^(?<prefix>[a-zA-Z][a-zA-Z0-9+\.\-]*://(?:[^/@]*@)?)(?<host>[^/:?#]+)(?<suffix>.*)$",
			RegexOptions.Singleline | RegexOptions.Compiled);

		private static readonly Regex BareHostRx = new Regex(
			@"^(?<host>[^/:?#]+)(?<suffix>[/:?#].*)?$",
			RegexOptions.Singleline | RegexOptions.Compiled);

		public static string ToUnicode(string url)
		{
			return TransformHost(url, LabelToUnicode);
		}

		public static string ToPunycode(string url)
		{
			return TransformHost(url, LabelToPunycode);
		}

		public static bool ContainsPunycode(string url)
		{
			if(string.IsNullOrEmpty(url)) return false;
			if(url.StartsWith("{", StringComparison.Ordinal)) return false;

			Match m = UrlWithSchemeRx.Match(url);
			if(!m.Success) m = BareHostRx.Match(url);
			if(!m.Success) return false;

			string host = m.Groups["host"].Value;
			string[] labels = host.Split('.');
			foreach(string label in labels)
			{
				if(label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		private static string TransformHost(string url, Func<string, string> labelTransform)
		{
			if(string.IsNullOrEmpty(url)) return url;
			if(url.StartsWith("{", StringComparison.Ordinal)) return url;

			Match m = UrlWithSchemeRx.Match(url);
			if(!m.Success) m = BareHostRx.Match(url);
			if(!m.Success) return url;

			string host = m.Groups["host"].Value;
			string newHost = TransformHostLabels(host, labelTransform);
			if(string.Equals(newHost, host, StringComparison.Ordinal)) return url;

			string prefix = m.Groups["prefix"].Success ? m.Groups["prefix"].Value : string.Empty;
			string suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : string.Empty;
			return prefix + newHost + suffix;
		}

		private static string TransformHostLabels(string host, Func<string, string> labelTransform)
		{
			string[] labels = host.Split('.');
			StringBuilder sb = new StringBuilder();
			for(int i = 0; i < labels.Length; ++i)
			{
				if(i > 0) sb.Append('.');
				sb.Append(labelTransform(labels[i]));
			}
			return sb.ToString();
		}

		private static string LabelToUnicode(string label)
		{
			if(label.Length == 0) return label;
			if(!label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)) return label;

			try { return Punycode.Decode(label.Substring(4)); }
			catch(Exception) { return label; } // Not valid Punycode - leave as-is
		}

		private static string LabelToPunycode(string label)
		{
			if(label.Length == 0) return label;
			if(label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)) return label;

			bool nonAscii = false;
			foreach(char c in label)
			{
				if(c > 127) { nonAscii = true; break; }
			}
			if(!nonAscii) return label; // Already plain ASCII, nothing to do

			try { return "xn--" + Punycode.Encode(label); }
			catch(Exception) { return label; } // Could not encode - leave as-is
		}
	}
}
