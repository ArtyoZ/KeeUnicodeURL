/*
	KeeUnicodeURL - KeePass plugin
	KeeUnicodeURL.cs

	Keeps KeePass' native URL field in Punycode for reliable Auto-Type/browser
	matching, while providing an editable Unicode representation in the entry
	editor and a Unicode URL column in the main entry list.
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;

namespace KeeUnicodeURL
{
	public sealed class KeeUnicodeURLExt : Plugin
	{
		private const string UpdateUrlValue =
			"https://raw.githubusercontent.com/ArtyoZ/KeeUnicodeURL/refs/heads/main/KeeUnicodeURL-version.txt";

		private const string UpdatePublicKey =
			"<RSAKeyValue><Modulus>1W/1lVlYTylBnzaHbUIIVR/VvEE5I3QbKSmgoMVFzPrW/qN51VD28xSIPVSio9nwqEBsR1R8xskFH+lLHgSZWEOP4vz0f3I9u83hGiGJqVdNXSfjlqFyKGiVkNCJ62PB3vn3xyxvH5qxp63vFaWmJEky5D/0/016e70fMNBUx3YDRvLSKPeFN5hSGMc2uhmEjlMXiXBWu/GUyi1sx+/ejQZqwcPqGIuP6MK0j56LV46wFyY4Nsph0hMDQ/wXpgWdDw1Kl7HS2qv9ASuEnC8bgldD+aazIvzlKP+A9tXHZfzPj+Oq8nr2ufdjG5bna4kl2vmEVcoARK/JIDAGnQ53cVWRB6p8TrrVRwXGlR00E6fH2vQ7oLmq5P/uvanS6mR7S9kymggdNZm5VblFUsE/K2ebRwxhYV9Mklg1vrqtFe9Pps0Ofjs7GHb9oCkdCaWqOhz4eH23Ofjl1LH227Ww9cpYRlwt3pFMifi6ND/fnSSxAQ0xmptCv12j+IALN8YublVTzP6l8RIeCEliPYePLKFWy1QS8iJvwp0pqwV7m2BYGmye5id6nzOtddLoybiepV5lCUETSTtfG9FImOtZ66ddf75kqkTFeTl3k0FC8nZNSNneMtK8aqYcLtHmoWMHdjgSGY9Vr2HxoR/XmaKVUHf2yh0Qg9E5TWwwc8zmcM8=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

		private const string ConfigShowField = "ShowUnicodeUrlField";
		private const string ConfigShowColumn = "ShowUnicodeUrlColumn";

		private readonly ConditionalWeakTable<PwEntryForm, EntryFormState> m_forms =
			new ConditionalWeakTable<PwEntryForm, EntryFormState>();
		private readonly List<EntryFormState> m_formStates = new List<EntryFormState>();

		private ToolStripMenuItem m_menuOptions = null;
		private ToolStripMenuItem m_menuShowField = null;
		private ToolStripMenuItem m_menuShowColumn = null;

		private UnicodeUrlColumnProvider m_columnProvider = null;
		private IPluginHost m_host = null;

		private static readonly BindingFlags PrivateInstance =
			BindingFlags.Instance | BindingFlags.NonPublic;

		public override string UpdateUrl
		{
			get { return UpdateUrlValue; }
		}

		public override bool Initialize(IPluginHost host)
		{
			if(host == null) return false;
			m_host = host;

			KeePass.Util.UpdateCheckEx.SetFileSigKey(UpdateUrl, UpdatePublicKey);

			bool showColumn = host.CustomConfig.GetBool(ConfigShowColumn, true);

			m_columnProvider = new UnicodeUrlColumnProvider();
			if(showColumn) host.ColumnProviderPool.Add(m_columnProvider);

			GlobalWindowManager.WindowAdded += this.OnWindowAdded;
			GlobalWindowManager.WindowRemoved += this.OnWindowRemoved;
			return true;
		}

		public override void Terminate()
		{
			GlobalWindowManager.WindowAdded -= this.OnWindowAdded;
			GlobalWindowManager.WindowRemoved -= this.OnWindowRemoved;

			if(m_host != null && m_columnProvider != null)
			{
				try { m_host.ColumnProviderPool.Remove(m_columnProvider); }
				catch(Exception) { }
			}

			m_columnProvider = null;
			m_host = null;
		}

		// Official, documented mechanism for a plugin to contribute an item
		// to KeePass's "Tools" menu - KeePass calls this itself and inserts
		// the returned item, so (unlike an earlier draft of this file) no
		// reflection into MainForm's private menu fields is needed here.
		public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
		{
			if(t != PluginMenuType.Main) return null; // Main = the "Tools" menu

			bool showField = (m_host != null) && m_host.CustomConfig.GetBool(ConfigShowField, true);
			bool showColumn = (m_host != null) && m_host.CustomConfig.GetBool(ConfigShowColumn, true);

			m_menuOptions = new ToolStripMenuItem("KeeUnicodeURL Options");

			m_menuShowField = new ToolStripMenuItem("Show Unicode URL field in the entry editor");
			m_menuShowField.CheckOnClick = true;
			m_menuShowField.Checked = showField;
			m_menuShowField.CheckedChanged += this.OnShowFieldChanged;

			m_menuShowColumn = new ToolStripMenuItem("Show Unicode URL column in the entry list");
			m_menuShowColumn.CheckOnClick = true;
			m_menuShowColumn.Checked = showColumn;
			m_menuShowColumn.CheckedChanged += this.OnShowColumnChanged;

			m_menuOptions.DropDownItems.Add(m_menuShowField);
			m_menuOptions.DropDownItems.Add(m_menuShowColumn);

			return m_menuOptions;
		}

		private void OnShowFieldChanged(object sender, EventArgs e)
		{
			if(m_host == null || m_menuShowField == null) return;
			bool enabled = m_menuShowField.Checked;
			m_host.CustomConfig.SetBool(ConfigShowField, enabled);

			foreach(EntryFormState state in GetFormStates())
			{
				state.Suppress = true;
				try { UpdateUnicodeField(state); }
				finally { state.Suppress = false; }
			}
		}

		private void OnShowColumnChanged(object sender, EventArgs e)
		{
			if(m_host == null || m_menuShowColumn == null || m_columnProvider == null) return;
			bool enabled = m_menuShowColumn.Checked;
			m_host.CustomConfig.SetBool(ConfigShowColumn, enabled);

			try
			{
				if(enabled)
				{
					m_host.ColumnProviderPool.Remove(m_columnProvider);
					m_host.ColumnProviderPool.Add(m_columnProvider);
				}
				else
				{
					m_host.ColumnProviderPool.Remove(m_columnProvider);
				}
				m_host.MainWindow.RefreshEntriesList();
			}
			catch(Exception) { }
		}

		private IEnumerable<EntryFormState> GetFormStates()
		{
			return m_formStates.ToArray();
		}

		private void OnWindowAdded(object sender, GwmWindowEventArgs e)
		{
			PwEntryForm f = e.Form as PwEntryForm;
			if(f == null) return;

			EntryFormState state;
			if(m_forms.TryGetValue(f, out state)) return;

			state = CreateEntryFormState(f);
			if(state == null) return;
			m_forms.Add(f, state);
			m_formStates.Add(state);

			f.Shown += this.OnEntryFormShown;
		}

		private void OnWindowRemoved(object sender, GwmWindowEventArgs e)
		{
			PwEntryForm f = e.Form as PwEntryForm;
			if(f == null) return;

			EntryFormState state;
			if(!m_forms.TryGetValue(f, out state)) return;

			UnhookEntryForm(state);
			f.Shown -= this.OnEntryFormShown;
			m_forms.Remove(f);
			m_formStates.Remove(state);
		}

		// Inserts a new editable "URL (Unicode):" row directly below the
		// real URL field, and captures enough information to shift the
		// controls below it down (and grow the dialog) only while the row
		// is actually visible. Every position/size involved is measured
		// from the *runtime* Bounds of the URL field and its neighbouring
		// controls - not from hard-coded pixel coordinates - so this does
		// not assume a specific KeePass version, DPI setting or theme.
		// Returns null (without having changed anything) if the
		// surrounding layout does not look the way this code expects.
		private EntryFormState CreateEntryFormState(PwEntryForm f)
		{
			try
			{
				TextBox url = GetUrlTextBox(f);
				if(url == null) return null;

				Control tab = url.Parent;
				if(tab == null) return null;

				Label urlCaption = null;
				foreach(Control c in tab.Controls)
				{
					Label lbl = c as Label;
					if(lbl == null) continue;
					if(Math.Abs(lbl.Top - url.Top) > 6) continue;
					if(lbl.Right > url.Left) continue;
					urlCaption = lbl;
					break;
				}
				if(urlCaption == null) return null;

				// Locate the nearest input field below the URL to determine the exact line spacing
				int rowStep = 0;
				int minTopBelow = int.MaxValue;
				foreach(Control c in tab.Controls)
				{
					if(c == url || c == urlCaption) continue;
					// Looking for input elements located below the original URL.
					if(c.Top >= url.Bottom - 2 && c.Left >= url.Left - 20)
					{
						if(c.Top < minTopBelow)
							minTopBelow = c.Top;
					}
				}

				if(minTopBelow != int.MaxValue)
				{
					rowStep = minTopBelow - url.Top; // The exact line spacing on this form
				}
				else
				{
					rowStep = url.Height + 7; // If there is nothing below
				}

				int labelTopOffset = urlCaption.Top - url.Top;
				int newTop = url.Top + rowStep;

				EntryFormState state = new EntryFormState();
				state.Form = f;
				state.Url = url;
				state.Tab = tab;

				// Looking for the Notes field (with a patch by Alex Vallat for KeeTheme)
				Control notesControl = f.Controls.Find("m_rtNotes", true).FirstOrDefault();
				if (notesControl != null && notesControl.Location == Point.Empty && notesControl.Parent != tab)
				{
					notesControl = notesControl.Parent;
				}
				state.NotesControl = notesControl;

				// Increase the TabIndex of all elements following the URL by 2 positions.
				int baseTabIndex = url.TabIndex;
				foreach (Control c in tab.Controls)
				{
					if (c.TabIndex > baseTabIndex)
					{
						c.TabIndex += 2;
					}
				}

				int maxLabelWidth = url.Left - urlCaption.Left - 3;

				state.Label = new Label();
				state.Label.AutoSize = urlCaption.AutoSize;
				state.Label.Font = urlCaption.Font;
				state.Label.Left = urlCaption.Left;

				if (maxLabelWidth > 0)
				{
					state.Label.AutoSize = false;
					state.Label.Width = maxLabelWidth;
				}
				else
				{
					state.Label.AutoSize = urlCaption.AutoSize;
					state.Label.Width = urlCaption.Width;
				}

				state.Label.Top = newTop + labelTopOffset;
				state.Label.Height = urlCaption.Height;
				state.Label.Anchor = urlCaption.Anchor;
				state.Label.TextAlign = urlCaption.TextAlign;
				state.Label.AutoEllipsis = true;
				state.Label.Text = "Unicode URL";
				state.Label.Name = "m_lblUnicodeUrl";
				state.Label.TabIndex = baseTabIndex + 1;
				state.Label.Visible = false;

				state.UnicodeUrl = new TextBox();
				state.UnicodeUrl.Left = url.Left;
				state.UnicodeUrl.Width = url.Width;
				state.UnicodeUrl.Top = newTop;
				state.UnicodeUrl.Height = url.Height;
				state.UnicodeUrl.Anchor = url.Anchor;
				state.UnicodeUrl.Font = url.Font;
				state.UnicodeUrl.ForeColor = url.ForeColor;
				state.UnicodeUrl.Name = "m_tbUnicodeUrl";
				state.UnicodeUrl.TabIndex = baseTabIndex + 2;
				state.UnicodeUrl.Visible = false;

				tab.Controls.Add(state.Label);
				tab.Controls.Add(state.UnicodeUrl);

				url.TextChanged += this.OnUrlTextChanged;
				state.UnicodeUrl.TextChanged += this.OnUnicodeUrlTextChanged;
				state.UnicodeUrl.Enter += this.OnUnicodeUrlEnter;

				return state;
			}
			catch(Exception) { return null; }
		}

		private void UnhookEntryForm(EntryFormState state)
		{
			if(state.Url != null) state.Url.TextChanged -= this.OnUrlTextChanged;
			if(state.UnicodeUrl != null)
			{
				state.UnicodeUrl.TextChanged -= this.OnUnicodeUrlTextChanged;
				state.UnicodeUrl.Enter -= this.OnUnicodeUrlEnter;
			}

			RestoreLayout(state);

			if(state.Tab != null)
			{
				if(state.Label != null) state.Tab.Controls.Remove(state.Label);
				if(state.UnicodeUrl != null) state.Tab.Controls.Remove(state.UnicodeUrl);
			}

			if(state.Label != null) state.Label.Dispose();
			if(state.UnicodeUrl != null) state.UnicodeUrl.Dispose();
		}

		private void OnEntryFormShown(object sender, EventArgs e)
		{
			PwEntryForm f = sender as PwEntryForm;
			if(f == null) return;

			EntryFormState state;
			if(!m_forms.TryGetValue(f, out state)) return;

			state.IsFormLoaded = true;

			state.Suppress = true;
			try
			{
				state.UnicodeUrl.ReadOnly = state.Url.ReadOnly;
				UpdateUnicodeField(state);
			}
			finally { state.Suppress = false; }
		}

		private static void InitializeShiftedControls(EntryFormState state)
		{
			if (state.InitializedShiftedControls) return;

			int urlBottom = state.Url.Bottom;
			int notesBottom = state.NotesControl != null ? state.NotesControl.Bottom : int.MaxValue;

			foreach (Control c in state.Tab.Controls)
			{
				if (c == state.Url || c == state.Label || c == state.UnicodeUrl) continue;

				// Exclude the original label for the URL field (located to the left of the URL).
				if (c is Label && c.Right <= state.Url.Left && Math.Abs(c.Top - state.Url.Top) <= 15) continue;

				// If the element is physically positioned below the URL field and not below the bottom of the Notes field
				// (this will encompass the Notes field itself, its "Notes:" label, and elements from other plugins)
				if (c.Top >= urlBottom - 5 && c.Top < notesBottom)
				{
					state.ShiftedControls.Add(c);
				}
			}
			
			state.InitializedShiftedControls = true;
		}

		private void OnUrlTextChanged(object sender, EventArgs e)
		{
			TextBox url = sender as TextBox;
			if(url == null) return;

			EntryFormState state = FindState(url);
			if(state == null || state.Suppress || !state.IsFormLoaded) return;

			state.Suppress = true;
			try { UpdateUnicodeField(state); }
			finally { state.Suppress = false; }
		}

		private void OnUnicodeUrlTextChanged(object sender, EventArgs e)
		{
			TextBox unicode = sender as TextBox;
			if(unicode == null) return;

			EntryFormState state = FindState(unicode);
			if(state == null || state.Suppress || state.UnicodeUrl == null || !state.IsFormLoaded) return;

			state.Suppress = true;
			try
			{
				string puny = IdnUrlUtil.ToPunycode(unicode.Text);
				if(!string.Equals(state.Url.Text, puny, StringComparison.Ordinal))
					state.Url.Text = puny;
				UpdateUnicodeField(state);
			}
			finally { state.Suppress = false; }
		}

		private void OnUnicodeUrlEnter(object sender, EventArgs e)
		{
			TextBox tb = sender as TextBox;
			if (tb == null) return;

			try
			{
				tb.BeginInvoke((MethodInvoker)delegate
				{
					if (tb != null && !tb.IsDisposed && tb.Focused)
					{
						tb.SelectAll();
					}
				});
			}
			catch (Exception) { }
		}

		private void UpdateUnicodeField(EntryFormState state)
		{
			// Dynamic color synchronization with the original URL field
			if (state.UnicodeUrl.ForeColor != state.Url.ForeColor)
			{
				state.UnicodeUrl.ForeColor = state.Url.ForeColor;
			}

			string url = state.Url.Text;
			bool show = m_host != null && m_host.CustomConfig.GetBool(ConfigShowField, true) &&
				IdnUrlUtil.ContainsPunycode(url);
			SetUnicodeFieldVisible(state, show);

			string unicode = IdnUrlUtil.ToUnicode(url);
			if(!string.Equals(state.UnicodeUrl.Text, unicode, StringComparison.Ordinal))
			{
				int start = state.UnicodeUrl.SelectionStart;
				state.UnicodeUrl.Text = unicode;
				if(start > state.UnicodeUrl.TextLength) start = state.UnicodeUrl.TextLength;
				state.UnicodeUrl.SelectionStart = start;
			}
		}

		private static void SetUnicodeFieldVisible(EntryFormState state, bool visible)
		{
			if(state == null || state.Visible == visible) return;
			if(!state.IsFormLoaded) return; // Don't move anything until the form appears.

			// Lazy initialization of the list of draggable elements before the first display
			if (visible && !state.InitializedShiftedControls)
			{
				InitializeShiftedControls(state);
			}

			state.Visible = visible;

			int insertSpace = state.UnicodeUrl.Bottom - state.Url.Bottom;
			int offset = visible ? insertSpace : -insertSpace;

			state.Tab.SuspendLayout();

			foreach(Control c in state.ShiftedControls)
			{
				if (c == state.NotesControl)
				{
					c.Top += offset;
					int newHeight = c.Height - offset;
					c.Height = newHeight > 10 ? newHeight : 10;
				}
				else
				{
					c.Top += offset;
				}
			}

			state.Label.Visible = visible;
			state.UnicodeUrl.Visible = visible;

			state.Tab.ResumeLayout(true);
		}

		private static void RestoreLayout(EntryFormState state)
		{
			if(state == null) return;
			// Returning the interface now simply involves hiding the field.
			SetUnicodeFieldVisible(state, false);
		}

		private EntryFormState FindState(Control control)
		{
			if(control == null) return null;
			PwEntryForm f = control.FindForm() as PwEntryForm;
			if(f == null) return null;

			EntryFormState state;
			return m_forms.TryGetValue(f, out state) ? state : null;
		}

		private static TextBox GetUrlTextBox(PwEntryForm f)
		{
			if(f == null) return null;
			try
			{
				FieldInfo fi = typeof(PwEntryForm).GetField("m_tbUrl", PrivateInstance);
				return (fi != null) ? fi.GetValue(f) as TextBox : null;
			}
			catch(Exception) { return null; }
		}

		private sealed class EntryFormState
		{
			public PwEntryForm Form;
			public Control Tab;
			public TextBox Url;
			public Label Label;
			public TextBox UnicodeUrl;
			public Control NotesControl;
			public bool Suppress;
			public bool Visible;
			public bool IsFormLoaded;
			public bool InitializedShiftedControls;
			public readonly List<Control> ShiftedControls = new List<Control>();
		}

		// Note: the field is named "TargetControl", not "Control" - naming
		// a field the same as a type it needs to reference (here,
		// System.Windows.Forms.Control) inside the same class causes the
		// field to shadow the type name within that class's own scope,
		// which is exactly what produced the CS0118 "is a 'field' but is
		// used like a 'type'" error in the constructor parameter below.
		private sealed class ControlBounds
		{
			public readonly Control TargetControl;
			public readonly Rectangle Bounds;

			public ControlBounds(Control control, Rectangle bounds)
			{
				TargetControl = control;
				Bounds = bounds;
			}
		}
	}
}
