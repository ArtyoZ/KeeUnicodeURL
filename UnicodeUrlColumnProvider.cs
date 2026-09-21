/*
	KeeUnicodeURL - KeePass plugin
	UnicodeUrlColumnProvider.cs

	Registers the "URL (Unicode)" entry-list column. Double-clicking a
	cell in this column performs exactly the same action as the built-in
	"URL" column: it opens the entry's real, stored URL (Punycode intact)
	through KeePass's own URL-opening pipeline (WinUtil.OpenUrl), which
	also takes care of URL Overrides, "cmd://" entries, {PLACEHOLDER}
	expansion, etc. It deliberately does NOT open the decoded Unicode
	string - that string is a display-only convenience and is not what
	the standard column would open either.
*/

using System;
using System.Windows.Forms;
using KeePass.UI;
using KeePassLib;

namespace KeeUnicodeURL
{
	public sealed class UnicodeUrlColumnProvider : ColumnProvider
	{
		private static readonly string[] Columns = new string[] { "Unicode URL" };

		public override string[] ColumnNames { get { return Columns; } }

		public override HorizontalAlignment TextAlign
		{
			get { return HorizontalAlignment.Left; }
		}

		public override string GetCellData(string strColumnName, PwEntry pe)
		{
			if(pe == null) return string.Empty;
			if(!string.Equals(strColumnName, Columns[0], StringComparison.Ordinal))
				return string.Empty;

			return IdnUrlUtil.ToUnicode(pe.Strings.ReadSafe(PwDefs.UrlField));
		}

		public override bool SupportsCellAction(string strColumnName)
		{
			return string.Equals(strColumnName, Columns[0], StringComparison.Ordinal);
		}

		public override void PerformCellAction(string strColumnName, PwEntry pe)
		{
			if(pe == null) return;
			if(!string.Equals(strColumnName, Columns[0], StringComparison.Ordinal)) return;

			string rawUrl = pe.Strings.ReadSafe(PwDefs.UrlField);
			if(string.IsNullOrEmpty(rawUrl)) return;

			// Same call the built-in "URL" column itself uses - the real,
			// stored (Punycode) value, not the decoded display string.
			KeePass.Util.WinUtil.OpenUrl(rawUrl, pe);
		}
	}
}
