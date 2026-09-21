/*
	KeeUnicodeURL - KeePass plugin
	Punycode.cs

	Minimal, self-contained implementation of the Punycode algorithm
	(RFC 3492), operating purely on the bootstring encoding itself.

	This deliberately does NOT use System.Globalization.IdnMapping.
	IdnMapping performs Punycode decoding/encoding PLUS a whole set of
	additional IDNA registration-policy checks on top of it (Std3 ASCII
	rules, the Bidi rule, Unicode character/category allow-lists, length
	limits, etc.). Those extra checks reject a number of real,
	registered, resolvable domain names - this is a long-standing,
	documented .NET behaviour (see e.g. dotnet/runtime issues #107900,
	#72962, #110499), not something specific to this plugin. A web
	browser only needs the label to be valid Punycode to display it in
	decoded form; it does not re-validate it against IDNA registration
	policy for that purpose, so neither does this plugin.

	Reference: https://www.rfc-editor.org/rfc/rfc3492
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace KeeUnicodeURL
{
	public static class Punycode
	{
		private const int Base = 36;
		private const int TMin = 1;
		private const int TMax = 26;
		private const int Skew = 38;
		private const int Damp = 700;
		private const int InitialBias = 72;
		private const int InitialN = 0x80;
		private const char Delimiter = '-';

		/// <summary>
		/// Decodes a Punycode payload (the part of an "xn--" label after
		/// the "xn--" prefix) into the original Unicode string.
		/// Throws FormatException on malformed input.
		/// </summary>
		public static string Decode(string input)
		{
			if(input == null) throw new ArgumentNullException("input");

			List<int> output = new List<int>();

			int lastDelim = input.LastIndexOf(Delimiter);
			int pos;
			if(lastDelim >= 0)
			{
				for(int bi = 0; bi < lastDelim; ++bi)
				{
					char c = input[bi];
					if(c >= 0x80) throw new FormatException("Non-basic code point in basic part.");
					output.Add(c);
				}
				pos = lastDelim + 1;
			}
			else pos = 0;

			int n = InitialN;
			int i = 0;
			int bias = InitialBias;

			while(pos < input.Length)
			{
				int oldi = i;
				int w = 1;

				for(int k = Base; ; k += Base)
				{
					if(pos >= input.Length) throw new FormatException("Unexpected end of input.");
					int digit = DecodeDigit(input[pos]);
					++pos;
					if(digit >= Base) throw new FormatException("Invalid Punycode digit.");

					// Overflow guard (domain labels are short, but be safe).
					if(digit > (int.MaxValue - i) / w) throw new FormatException("Overflow.");
					i += digit * w;

					int t = (k <= bias) ? TMin : ((k >= bias + TMax) ? TMax : (k - bias));
					if(digit < t) break;

					if(w > int.MaxValue / (Base - t)) throw new FormatException("Overflow.");
					w *= (Base - t);
				}

				bias = Adapt(i - oldi, output.Count + 1, (oldi == 0));
				if(i / (output.Count + 1) > int.MaxValue - n) throw new FormatException("Overflow.");
				n += i / (output.Count + 1);
				i %= (output.Count + 1);

				if((n < 0) || (n > 0x10FFFF)) throw new FormatException("Invalid code point.");
				output.Insert(i, n);
				++i;
			}

			StringBuilder sb = new StringBuilder(output.Count);
			for(int j = 0; j < output.Count; ++j)
				sb.Append(char.ConvertFromUtf32(output[j]));
			return sb.ToString();
		}

		/// <summary>
		/// Encodes a Unicode string into a Punycode payload (without the
		/// "xn--" prefix). Throws FormatException on invalid input
		/// (unpaired surrogates).
		/// </summary>
		public static string Encode(string input)
		{
			if(input == null) throw new ArgumentNullException("input");

			List<int> cps = ToCodePoints(input);

			StringBuilder output = new StringBuilder();
			foreach(int cp in cps)
			{
				if(cp < 0x80) output.Append((char)cp);
			}

			int b = output.Length;
			int h = b;
			if(b > 0) output.Append(Delimiter);

			int n = InitialN;
			int delta = 0;
			int bias = InitialBias;

			while(h < cps.Count)
			{
				int m = int.MaxValue;
				foreach(int cp in cps)
				{
					if((cp >= n) && (cp < m)) m = cp;
				}

				if(m - n > (int.MaxValue - delta) / (h + 1)) throw new FormatException("Overflow.");
				delta += (m - n) * (h + 1);
				n = m;

				foreach(int cp in cps)
				{
					if(cp < n) ++delta;

					if(cp == n)
					{
						int q = delta;
						for(int k = Base; ; k += Base)
						{
							int t = (k <= bias) ? TMin : ((k >= bias + TMax) ? TMax : (k - bias));
							if(q < t) break;
							output.Append(EncodeDigit(t + ((q - t) % (Base - t))));
							q = (q - t) / (Base - t);
						}
						output.Append(EncodeDigit(q));

						bias = Adapt(delta, h + 1, (h == b));
						delta = 0;
						++h;
					}
				}

				++delta;
				++n;
			}

			return output.ToString();
		}

		private static int Adapt(int delta, int numPoints, bool firstTime)
		{
			delta = firstTime ? (delta / Damp) : (delta / 2);
			delta += delta / numPoints;

			int k = 0;
			while(delta > ((Base - TMin) * TMax) / 2)
			{
				delta /= (Base - TMin);
				k += Base;
			}

			return k + (((Base - TMin + 1) * delta) / (delta + Skew));
		}

		private static int DecodeDigit(char c)
		{
			if((c >= '0') && (c <= '9')) return (c - '0') + 26;
			if((c >= 'A') && (c <= 'Z')) return (c - 'A');
			if((c >= 'a') && (c <= 'z')) return (c - 'a');
			return Base; // Invalid; caller checks against Base
		}

		private static char EncodeDigit(int d)
		{
			if((d < 0) || (d > 35)) throw new FormatException("Invalid digit.");
			return (char)((d < 26) ? ('a' + d) : ('0' + (d - 26)));
		}

		private static List<int> ToCodePoints(string s)
		{
			List<int> cps = new List<int>(s.Length);
			int idx = 0;
			while(idx < s.Length)
			{
				if(char.IsHighSurrogate(s[idx]) && (idx + 1 < s.Length) &&
					char.IsLowSurrogate(s[idx + 1]))
				{
					cps.Add(char.ConvertToUtf32(s[idx], s[idx + 1]));
					idx += 2;
				}
				else
				{
					if(char.IsSurrogate(s[idx])) throw new FormatException("Unpaired surrogate.");
					cps.Add(s[idx]);
					idx += 1;
				}
			}
			return cps;
		}
	}
}
