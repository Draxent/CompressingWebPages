using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.MemoryMappedFiles;

namespace CompressingWebPages
{
	class WebPage
	{
		private const long PRIME_NUMBER = 994534132561;
		private const int QGRAMS = 25;
		private const int MAX_WINDOW = 500;

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

		public uint ID { get; private set; }
		public long Length { get; private set; }
		public long StartPage { get; private set; }
		public long NewStartPage { get; set; }
		public ulong[] Signature { get; private set; }
		public string URL { get; private set; }

		private long htmlRelative, endPageRelative, endPage;
		private ScanningFile sf = null;

		#region CONSTRUCTOR
		public WebPage(uint idPage, ScanningFile sf, FastMurmurHash[] permutations, long endLastPage)
		{
			Tuple<bool, long> t;
			this.ID = idPage;
			this.sf = sf;

			// Finds the start of the web page
			t = sf.Search(new byte[][] { STARTPAGE }, 1, endLastPage);
			// There are no more page. Reached End of File (EOF).
			if (!t.Item1)
				throw new EndOfStreamException();
			long startPageRelative = t.Item2;
			this.StartPage = sf.AbsoluteLocation(startPageRelative);

			// Get the WARC size
			long sizeEnd = sf.Search(WHITE_SPACE, startPageRelative + 9).Item2 - 1;
			long warcSize = Convert.ToInt64(sf.Copy(startPageRelative + 9, sizeEnd));

			// Get the URL
			long urlStart = sf.Search(new byte[][] { RESPONSE }, 1, sizeEnd).Item2 + 9;
			long urlEnd = sf.Search(WHITE_SPACE, urlStart + 1).Item2 - 1;
			this.URL = sf.Copy(urlStart, urlEnd);

			// Finds the content-type of the page
			t = sf.Search(new byte[][] { CONTENT_TYPE }, 1, urlEnd, null, MAX_WINDOW);
			long contentTypeStart = t.Item2, contentTypeEnd;
			bool ishtmlpage = t.Item1;
			if (t.Item1)
			{
				contentTypeEnd = sf.Search(WHITE_SPACE, contentTypeStart + 14).Item2 - 1;
				ishtmlpage = sf.Copy(contentTypeStart, contentTypeEnd).ToLower().Contains("html");
			}

			// If we could find the content-type and it is of html type we can looking for the <html> tag
			if (ishtmlpage)
			{
				// Finds the pointer to the <html> (or <head>/<body>) part.
				t = sf.Search(new byte[][] { HTML1, HTML2, HEAD1, HEAD2, BODY1, BODY2 }, 6, urlEnd, STARTPAGE);

				// If it can find it, means that we have an HTML document
				if (t.Item1)
				{
					this.Signature = new ulong[sf.SketchVectorSize];
					this.htmlRelative = t.Item2;
					this.CalculateSignature(permutations);
				}
				ishtmlpage = t.Item1;
			}

			// If we could not find the content-type or it is not html type, or we could not find the <html> tag
			// we do NOT compute the signature and we ignore the page
			if (!ishtmlpage)
			{
				this.htmlRelative = startPageRelative + 8;
				this.endPageRelative = sf.Search(new byte[][] { STARTPAGE }, 1, startPageRelative + warcSize).Item2 - 1;
				this.endPage = sf.AbsoluteLocation(this.endPageRelative);
			}

			this.Length = this.endPage - this.StartPage + 1;
		}
		#endregion

		#region GetRelativeEndPage
		public long GetRelativeEndPage()
		{
			return this.endPageRelative;
		}
		#endregion

		#region GetContent
		public MemoryMappedViewStream GetContent(bool html = false)
		{
			return sf.ReadFile(this.StartPage, this.Length);
		}
		#endregion

		#region WordSegmentation
		// Scan the html content and divides it in words
		private IEnumerator<Tuple<long, int>> WordSegmentation()
		{
			bool addWord = false;
			long i = this.htmlRelative, start = 0, end = 0;

			// Continues until it reach another web page or the EOF
			while (!sf.EOF(i) && !sf.MatchBytes(i, STARTPAGE))
			{
				start = i;

				// Found SCRIPT
				if (sf.MatchBytes(i, OPEN_SCRIPT1, OPEN_SCRIPT2))
				{
					// Skips all until it find the tag closure </script> (Supposing that the Web Page are well formed)
					i = sf.Search(new byte[][] { CLOSE_SCRIPT1, CLOSE_SCRIPT2 }, 2, start, STARTPAGE).Item2 + 8;
					addWord = true;
				}
				// Found TAG
				else if ((sf.Match(i, OPEN_BRACKET)) && (!sf.Match(i + 1, EXCLAMATION_MARK)))
				{
					// Skips all until it find a tag closure (Supposing that the Web Page are well formed)
					i = sf.Search(CLOSE_BRACKET, start, STARTPAGE).Item2;
					addWord = true;
				}
				// Found WORD
				else if (!Char.IsWhiteSpace(sf.GetChar(i)))
				{
					// Skips all until it find a space or a tag opening (so a "<" and not a "<!")
					// We don't check the EOF since we expected to reach the TAG </html> somewhere,
					// if the file end before GetChar will raise an exception 
					while (!(Char.IsWhiteSpace(sf.GetChar(i)) || (sf.Match(i, OPEN_BRACKET) && (!sf.Match(i + 1, EXCLAMATION_MARK)))))
						i++;
					i--; addWord = true;
				}

				if (addWord)
				{
					end = i; addWord = false;
					// A word is represented by a pointer to the word itself in the html Content and by its size
					yield return new Tuple<long, int>(start, (int)(end - start + 1));
					//string word = System.Text.Encoding.Default.GetString(sf.Copy(start, end + 1));

					// If we reach this point should mean that we have already used the discovered word, so we can free the buffer.
					i = sf.Free(end);
				}

				i++;
			}

			// Calculates the position of the end of the page.
			this.endPageRelative = i - 1;
			this.endPage = sf.AbsoluteLocation(this.endPageRelative);
		}
		#endregion

		#region FingerPrint
		private ulong FingerPrint(Tuple<long, int> word)
		{
			ulong sig = 0;
			long start = word.Item1, end = start + word.Item2;

			for (long k = start; k < end; k++)
			{
				//Karp-Rabin hashing
				sig = (sig << 8) + (ulong)sf.Get(k);
				if (sig > PRIME_NUMBER) { sig %= PRIME_NUMBER; }
			}

			return sig;
		}
		#endregion

		#region CumulativeFingerPrint
		private ulong CumulativeFingerPrint(List<Tuple<ulong, int>> bufferFingers, int q)
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
		private List<ulong> Shingling()
		{
			// A shingle is represented by the fingerprint and the total size of about QGRAMS
			List<ulong> shingles = new List<ulong>();
			IEnumerator<Tuple<long, int>> words = WordSegmentation();
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
						shingles.Add(this.CumulativeFingerPrint(bufferFingers, sizeBuffer));
						return shingles;
					}

					//string ss = System.Text.Encoding.Default.GetString(sf.Copy(s, s + l));
					bufferFingers.Add(new Tuple<ulong, int>(FingerPrint(words.Current), words.Current.Item2));
					gramCounter += words.Current.Item2;
					sizeBuffer++;
				}

				shingles.Add(this.CumulativeFingerPrint(bufferFingers, sizeBuffer));

				// Remove first word
				gramCounter -= bufferFingers[0].Item2;
				bufferFingers.RemoveAt(0);
				sizeBuffer--;
			}
		}
		#endregion

		#region CalculateSignature
		private void CalculateSignature(FastMurmurHash[] permutations)
		{
			List<ulong> shingles = this.Shingling();

			int k = 0;
			foreach (FastMurmurHash pi in permutations)
			{
				ulong min = ulong.MaxValue;
				foreach (ulong shingle in shingles)
				{
					ulong s = pi.Hash(shingle);
					if (s < min) min = s;
				}
				Signature[k] = min;
				k++;
			}
		}
		#endregion
	}
}
