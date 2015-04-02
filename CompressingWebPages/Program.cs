using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.MemoryMappedFiles;

namespace CompressingWebPages
{
	class Program
	{
		static Stopwatch stopWatch;
		static LSH lsh;
		static List<WebPage> webpages;
		static StreamWriter result;

		#region ComputePrintOrder
		static void ComputePrintOrder(int id, ref long offset, StreamWriter log)
		{
			WebPage wp = webpages[id];
			wp.NewStartPage = offset;
			webpages[id] = wp;

			offset += webpages[id].Length;
			log.Write(id);
			log.Write(',');
		}
		#endregion

		#region PermutingContainer
		static void PermutingContainer(byte[] content, byte[] permutedContainer, int[] positions, int start, int end, int[] newOrder)
		{
			int offset = 0;
			for (int i = 0; i <= (end - start); i++)
			{
				positions[i] = offset;
				offset += webpages[i + start].Length;
			}

			offset = 0;
			foreach (int x in newOrder)
			{
				Array.Copy(content, positions[x % GlobalVars.PIECE_SORTING_WEBPAGES], permutedContainer, offset, webpages[x].Length);
				offset += webpages[x].Length;
			}
		}
		#endregion

		#region WriteLine
		static void WriteLine(string s = "")
		{
			Console.WriteLine(s);
			result.WriteLine(s);
		}
		#endregion

		#region Main
		static void Main()
		{
			stopWatch = new Stopwatch();

			List<long> posChunkWebPages = new List<long>();
			webpages = new List<WebPage>();
			result = new StreamWriter(GlobalVars.WORKING_DIRECTORY + "Results2.txt");

			#region FIRST PHASE

			WriteLine("FIRST PHASE: COLLECTING THE WEB PAGES INFORMATION...");
			stopWatch.Start();

			long sizeInput = new FileInfo(GlobalVars.WORKING_DIRECTORY + "Input.warc").Length;
			ScanningFile sf = new ScanningFile(GlobalVars.WORKING_DIRECTORY + "Input.warc", sizeInput);
			IEnumerator<ProcessWebPage> wps = sf.WebPageSegmentation();

			while (wps.MoveNext())
			{
				if (webpages.Count % GlobalVars.SIZE_CHUNK_WEBPAGES == 0)
					posChunkWebPages.Add(wps.Current.WebPage.StartPage);

				webpages.Add(wps.Current.WebPage);
			}
			posChunkWebPages.Add(sizeInput - 1);

			sf.Dispose();
			stopWatch.Stop();
			WriteLine("|\tPages processed: " + webpages.Count + ".");
			WriteLine("|\tTime: " + (int)stopWatch.Elapsed.TotalSeconds + " seconds.");
			WriteLine("END FIRST PHASE.");
			WriteLine();
				
			#endregion

			#region SECOND PHASE

			WriteLine("SECOND PHASE: CALCULATE SIGNATURES AND INSERT INTO LSH...");
			stopWatch.Start();

			// Pre-calculate permutations
			FastMurmurHash[] permutations = new FastMurmurHash[GlobalVars.SKETCH_VECTOR_SIZE];
			for (uint i = 0; i < GlobalVars.SKETCH_VECTOR_SIZE; i++)
				permutations[i] = new FastMurmurHash(i);

			ScanningWebPages swp = new ScanningWebPages(GlobalVars.WORKING_DIRECTORY + "Input.warc", posChunkWebPages);
			HashSet<int> categWebPages = new HashSet<int>(), unCategWebPages = new HashSet<int>();
			lsh = new LSH();

			for (int i = 0; i < webpages.Count; i++)
			{
				WebPage wp = webpages[i];

				if (wp.IsHTML)
				{
					int contentLength = wp.Length - (int)(wp.HTMLStart - wp.StartPage);
					// margin space 2MB
					swp.WebPageContent(i, wp.HTMLStart, contentLength, 0x200000);

					wp.Signature = ProcessWebPage.CalculateSignature(swp.Container, contentLength, permutations);

					lsh.AddDocument(i, wp.Signature);
					categWebPages.Add(i);
					webpages[i] = wp;
				}
				else
					unCategWebPages.Add(i);
			}

			stopWatch.Stop();
			WriteLine("|\tTime: " + (int)stopWatch.Elapsed.TotalSeconds + " seconds.");
			WriteLine("END SECOND PHASE.");
			WriteLine();

			#endregion

			#region THIRD PHASE

			WriteLine("THIRD PHASE: CALCULATE PERMUTATIONS...");
			stopWatch.Restart();

			StreamWriter log = new StreamWriter(GlobalVars.WORKING_DIRECTORY + "PageOrder.txt", false);
			long offset = 0;
			while (categWebPages.Count > 0)
			{
				foreach (int id in lsh.UnionFind(webpages, categWebPages.First()).OrderBy(id => webpages[id].URL))
				{
					ComputePrintOrder(id, ref offset, log);
					categWebPages.Remove(id);
				}
				log.Write('\n');
			}
			categWebPages = null;

			foreach (int id in unCategWebPages.OrderBy(id => webpages[id].URL))
				ComputePrintOrder(id, ref offset, log);
			unCategWebPages = null;
			log.Write('\n');
			log.Close();
			stopWatch.Stop();

			WriteLine("|\tTime: " + (int)stopWatch.Elapsed.TotalSeconds + " seconds.");
			WriteLine("END THIRD PHASE.");
			WriteLine();

			#endregion

			#region FOURTH PHASE

			WriteLine("FOURTH PHASE: WRITING INTO THE OUPUT FILE...");
			stopWatch.Restart();

			// Delete signature and URLs from webpages -> we don't need them anymore
			for (int i = 0; i < webpages.Count; i++)
			{
				WebPage wp = webpages[i];
				wp.Signature = null;
				wp.URL = null;
				webpages[i] = wp;
			}

			MemoryMappedFile output = MemoryMappedFile.CreateFromFile(GlobalVars.WORKING_DIRECTORY + "Output.warc", FileMode.Create, "OUTPUT", sizeInput);

			int q = (int)Math.Ceiling((double)(webpages.Count / GlobalVars.PIECE_SORTING_WEBPAGES));
			int r = webpages.Count % GlobalVars.PIECE_SORTING_WEBPAGES;
			int[] pages2Write = new int[GlobalVars.PIECE_SORTING_WEBPAGES];
			int[] positions = new int[GlobalVars.PIECE_SORTING_WEBPAGES];
			for (int i = 0; i < q; i++)
			{
				int start = i * GlobalVars.PIECE_SORTING_WEBPAGES, end;

				if (i == q - 1 && r != 0) end = start + r - 1;
				else end = start + GlobalVars.PIECE_SORTING_WEBPAGES - 1;

				// Read 1K web pages
				int length = (int)(webpages[end].StartPage + webpages[end].Length - webpages[start].StartPage);
				// margin space = 100MB
				swp.WebPageContent(start, webpages[start].StartPage, length, 0x6400000, GlobalVars.PIECE_SORTING_WEBPAGES);

				// Sort 1K web pages based on NewStartPage
				for (int j = 0; j <= (end - start); j++)
					pages2Write[j] = webpages[j + start].ID;
				Array.Sort(pages2Write, delegate(int id1, int id2) { return webpages[id1].NewStartPage.CompareTo(webpages[id2].NewStartPage); });
				PermutingContainer(swp.Container, swp.PermutedContainer, positions, start, end, pages2Write);

				// Write 1K web pages on file
				int containerOffset = 0;
				for (int j = 0; j <= (end - start); j++)
				{
					WebPage wp = webpages[pages2Write[j]];
					using (MemoryMappedViewStream outputStream = output.CreateViewStream(wp.NewStartPage, wp.Length))
						outputStream.WriteAsync(swp.PermutedContainer, containerOffset, wp.Length);
					containerOffset += wp.Length;
				}
			}
			output.Dispose();
			stopWatch.Stop();
			swp.Dispose();

			WriteLine("|\tTime: " + (int)stopWatch.Elapsed.TotalSeconds + " seconds.");
			WriteLine("END FOURTH PHASE.");
			WriteLine();
			result.Close();
				
			#endregion
			
			Console.ReadLine();
		}
		#endregion
	}
}