using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompressingWebPages
{

	[Flags]
	public enum Info { IsHTML = 1, StartPage = 2, HTMLStart = 4, Length = 8, NewStartPage = 16, URL = 32, Signature = 64 }

	public struct WebPage
	{
		public int ID;
		public bool IsHTML;
		public long StartPage;
		public long HTMLStart;
		public int Length;
		public long NewStartPage;
		public string URL;
		public ulong[] Signature;
	}

	class ProcessWebPage
	{
		private const long PRIME_NUMBER = 994534132561;
		private const int QGRAMS = 25;
		private const int MAX_WINDOW_SEARCH = 1024;

		#region List Bytes
		// The list below shows all bytes arrays used searching in the web-page
		private const byte OPEN_BRACKET = 60;
		private const byte CLOSE_BRACKET = 62;
		private const byte EXCLAMATION_MARK = 33;
		private const byte WHITE_SPACE = 32;
		private static readonly byte[] RESPONSE = new byte[] { 114, 101, 115, 112, 111, 110, 115, 101, 32 }; // "response "
		private static readonly byte[] CONTENT_TYPE = new byte[] { 99, 111, 110, 116, 101, 110, 116, 45, 116, 121, 112, 101, 58, 32 }; // "content-type: "
		private static readonly byte[] STARTPAGE = new byte[] { 119, 97, 114, 99, 47, 48, 46, 57 }; // "warc/0.9"
		private static readonly byte[] HTML1 = new byte[] { 60, 104, 116, 109, 108 }; // "<html"
		private static readonly byte[] HTML2 = new byte[] { 60, 72, 84, 77, 76 }; // "<HTML"
		private static readonly byte[] HEAD1 = new byte[] { 60, 104, 101, 97, 100 }; // "<head"
		private static readonly byte[] HEAD2 = new byte[] { 60, 72, 69, 65, 68 }; // "<HEAD"
		private static readonly byte[] BODY1 = new byte[] { 60, 98, 111, 100, 121 }; // "<body"
		private static readonly byte[] BODY2 = new byte[] { 60, 66, 79, 68, 89 }; // "<BODY"
		private static readonly byte[] OPEN_SCRIPT1 = new byte[] { 60, 115, 99, 114, 105, 112, 116 }; // "<script"
		private static readonly byte[] OPEN_SCRIPT2 = new byte[] { 60, 83, 67, 82, 73, 80, 84 }; // "<SCRIPT"
		private static readonly byte[] CLOSE_SCRIPT1 = new byte[] { 60, 47, 115, 99, 114, 105, 112, 116, 62 }; // "</script>"
		private static readonly byte[] CLOSE_SCRIPT2 = new byte[] { 60, 47, 83, 67, 82, 73, 80, 84, 62 }; // "</SCRIPT>"
		#endregion

		public WebPage WebPage;

		#region CONSTRUCTOR
		public ProcessWebPage(int idPage, ScanningFile sf, long endLastPage)
		{
			Tuple<bool, long> t;
			this.WebPage = ProcessWebPage.InitWebPage(idPage);

			// Finds the start of the web page
			t = sf.Search(new byte[][] { STARTPAGE }, endLastPage, MAX_WINDOW_SEARCH);
			// There are no more page. Reached End of File (EOF).
			if (!t.Item1)
				throw new EndOfStreamException();
			long sp = t.Item2;
			this.WebPage.StartPage = sf.AbsoluteLocation(sp);

			// Get the WARC size
			long sizeEnd = sf.Search(WHITE_SPACE, sp + 9, MAX_WINDOW_SEARCH).Item2 - 1;
			this.WebPage.Length = Convert.ToInt32(sf.Copy(sp + 9, sizeEnd));

			// Get the URL
			long urlStart = sf.Search(new byte[][] { RESPONSE }, sizeEnd).Item2 + 9;
			long urlEnd = sf.Search(WHITE_SPACE, urlStart + 1).Item2 - 1;
			this.WebPage.URL = sf.Copy(urlStart, urlEnd);

			// Finds the content-type of the page
			t = sf.Search(new byte[][] { CONTENT_TYPE }, urlEnd, this.WebPage.Length - (urlEnd - sp));
			long contentTypeStart = t.Item2 + 14, contentTypeEnd;
			this.WebPage.IsHTML = t.Item1;
			if (t.Item1)
			{
				contentTypeEnd = sf.Search(WHITE_SPACE, contentTypeStart, this.WebPage.Length - (contentTypeStart - sp)).Item2 - 1;
				this.WebPage.IsHTML = sf.Copy(contentTypeStart, contentTypeEnd).ToLower().Contains("html");
			}

			// If we could find the content-type and it is of html type we can looking for the <html> tag
			if (this.WebPage.IsHTML)
			{
				// Finds the pointer to the <html> (or <head>/<body>) part.
				t = sf.Search(new byte[][] { HTML1, HTML2, HEAD1, HEAD2, BODY1, BODY2 }, contentTypeStart, this.WebPage.Length - (contentTypeStart - sp));

				this.WebPage.IsHTML = t.Item1;
				if (this.WebPage.IsHTML)
					this.WebPage.HTMLStart = sf.AbsoluteLocation(t.Item2);
			}
		}
		#endregion

		#region InitWebPage
		public static WebPage InitWebPage(int id)
		{
			WebPage wp = new WebPage();
			wp.ID = id;
			wp.IsHTML = false;
			wp.StartPage = -1;
			wp.HTMLStart = -1;
			wp.Length = -1;
			wp.NewStartPage = -1;
			wp.Signature = null;
			wp.URL = null;
			return wp;
		}
		#endregion

		#region Match
		private static bool Match(byte[] content, int length, int i, byte b)
		{
			if (i >= length) return false;
			return (content[i] == b);
		}
		private static bool MatchBytes(byte[] content, int length, int i, byte[] bb)
		{
			foreach (byte b in bb)
				if (!Match(content, length, i++, b)) return false;
			return true;
		}
		private static bool MatchBytes(byte[] content, int length, int i, byte[] bb1, byte[] bb2)
		{
			bool matched = MatchBytes(content, length, i, bb1);
			if (!matched)
				matched = MatchBytes(content, length, i, bb2);
			return matched;
		}
		#endregion

		#region Search
		public static int Search(byte[] content, int length, byte patternByte, int offset = 0)
		{
			for (int i = offset; i < length; i++)
				if (content[i] == patternByte) return i;
			return -1;
		}
		public static int Search(byte[] content, int length, byte[] pattern1, byte[] pattern2 = null, int offset = 0)
		{
			for (int i = offset; i < length; i++)
			{
				bool found = true;
				// Searches the pattern
				for (int j = 0; j < pattern1.Length; j++)
					if (content[i + j] != pattern1[j]) { found = false; break; }
				if (found) return i;

				if (pattern2 != null)
				{
					found = true;
					// Searches the pattern
					for (int j = 0; j < pattern2.Length; j++)
						if (content[i + j] != pattern2[j]) { found = false; break; }
					if (found) return i;
				}
			}
			return -1;
		}
		#endregion

		#region WordSegmentation
		// Scan the html content and divides it in words
		private static IEnumerator<Tuple<int, int>> WordSegmentation(byte[] content, int length)
		{
			bool addWord = false;
			int start = 0, end = 0;

			for (int i = 0; i < length; i++)
			{
				start = i;

				// Found SCRIPT
				if (ProcessWebPage.MatchBytes(content, length, i, OPEN_SCRIPT1, OPEN_SCRIPT2))
				{
					// Skips all until it find the tag closure </script> (Supposing that the Web Page are well formed)
					i = ProcessWebPage.Search(content, length, CLOSE_SCRIPT1, CLOSE_SCRIPT2, i) + 8;
					addWord = true;
				}
				// Found TAG
				else if (ProcessWebPage.Match(content, length, i, OPEN_BRACKET) && (!ProcessWebPage.Match(content, length, i + 1, EXCLAMATION_MARK)))
				{
					// Skips all until it find a tag closure (Supposing that the Web Page are well formed)
					i = ProcessWebPage.Search(content, length, CLOSE_BRACKET, i);
					addWord = true;
				}
				// Found WORD
				else if (!Char.IsWhiteSpace((char)content[i]))
				{
					// Skips all until it find a space or a tag opening (so a "<" and not a "<!")
					// We don't check the EOF since we expected to reach the TAG </html> somewhere,
					// if the file end before GetChar will raise an exception 
					while (i < length && !(Char.IsWhiteSpace((char)content[i]) || (ProcessWebPage.Match(content, length, i, OPEN_BRACKET) && (!ProcessWebPage.Match(content, length, i + 1, EXCLAMATION_MARK)))))
						i++;
					i--; addWord = true;
				}

				if (addWord)
				{
					end = i; addWord = false;

					// If there we found an error interrupt the process
					if (end <= start) break;

					// A word is represented by a pointer to the start of the word itself and its length
					yield return new Tuple<int, int>(start, end - start + 1);
				}
			}
		}
		#endregion

		#region FingerPrint
		private static ulong FingerPrint(byte[] content, Tuple<int, int> word)
		{
			ulong sig = 0;
			for (long k = word.Item1; k < word.Item2 - word.Item1; k++)
			{
				//Karp-Rabin hashing
				sig = (sig << 8) + (ulong)content[k];
				if (sig > PRIME_NUMBER) { sig %= PRIME_NUMBER; }
			}
			return sig;
		}
		#endregion

		#region CumulativeFingerPrint
		private static ulong CumulativeFingerPrint(List<Tuple<ulong, int>> bufferFingers, int q)
		{
			ulong sig = 0;

			for (int j = 0; j < q; j++)
			{
				sig = (sig << 24) + bufferFingers[j].Item1;
				if (sig > PRIME_NUMBER) { sig %= PRIME_NUMBER; }
			}
			return sig;
		}
		#endregion

		#region Shingling
		private static List<ulong> Shingling(byte[] content, int length)
		{
			// A shingle is represented by the fingerprint and the total size of about QGRAMS
			List<ulong> shingles = new List<ulong>();
			IEnumerator<Tuple<int, int>> words = ProcessWebPage.WordSegmentation(content, length);
			List<Tuple<ulong, int>> bufferFingers = new List<Tuple<ulong, int>>();

			int gramCounter = 0, sizeBuffer = 0;

			while (true)
			{
				while (gramCounter < QGRAMS)
				{
					bool canMove = words.MoveNext();

					// Reached EOP (End Of Page) before QGRAMS
					if (!canMove)
					{
						shingles.Add(ProcessWebPage.CumulativeFingerPrint(bufferFingers, sizeBuffer));
						return shingles;
					}

					bufferFingers.Add(new Tuple<ulong, int>(ProcessWebPage.FingerPrint(content, words.Current), words.Current.Item2));
					gramCounter += words.Current.Item2;
					sizeBuffer++;
				}

				shingles.Add(ProcessWebPage.CumulativeFingerPrint(bufferFingers, sizeBuffer));

				// Remove first word
				gramCounter -= bufferFingers[0].Item2;
				bufferFingers.RemoveAt(0);
				sizeBuffer--;
			}
		}
		#endregion

		#region CalculateSignature
		public static ulong[] CalculateSignature(byte[] content, int length, FastMurmurHash[] permutations)
		{
			ulong[] signature = new ulong[GlobalVars.SKETCH_VECTOR_SIZE];
			List<ulong> shingles = ProcessWebPage.Shingling(content, length);

			int k = 0;
			foreach (FastMurmurHash pi in permutations)
			{
				ulong min = ulong.MaxValue;
				foreach (ulong shingle in shingles)
				{
					ulong s = pi.Hash(shingle);
					if (s < min) min = s;
				}
				signature[k] = min;
				k++;
			}

			return signature;
		}
		#endregion
	}
}
