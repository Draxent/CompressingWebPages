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
		public const int SKETCHVECTORSIZE = 20;
		// LSH Threshold t = (1/b)^(1/r), that in this case, where b = 37 and r = 3, the threshold t = 30%
		public const int LSH_NUMITER = 37; // b
		public const int LSH_NUMPICK = 3; // r
		public const string WORKING_DIRECTORY = @"C:\Users\Federico\Desktop\BigData\Project2\Results\3000K\";

		public static Stopwatch stopWatch;

		static void ComputePrintOrder(WebPage wp, ref long offset, StreamWriter log)
		{
			wp.NewStartPage = offset;
			offset += wp.Length;
			log.Write(wp.ID);
			log.Write(',');
		}

		static void Main()
		{
			uint k = 1;
			stopWatch = new Stopwatch();

			FastMurmurHash[] permutations = new FastMurmurHash[SKETCHVECTORSIZE];
			for (uint i = 0; i < SKETCHVECTORSIZE; i++)
				permutations[i] = new FastMurmurHash(i);

			LSH lsh = new LSH(SKETCHVECTORSIZE, LSH_NUMITER, LSH_NUMPICK);

			try
			{
				ScanningFile sf = new ScanningFile(WORKING_DIRECTORY + "Input.warc", permutations, SKETCHVECTORSIZE);
				List<WebPage> webpages = new List<WebPage>();
				HashSet<WebPage> categWebPages = new HashSet<WebPage>(), unCategWebPages = new HashSet<WebPage>();
				IEnumerator<WebPage> wps = sf.WebPageSegmentation();

				#region FIRST PHASE

				Console.WriteLine("FIRST PHASE: PROCESSING WEB PAGES...");
				stopWatch.Start();

				// k = 2.947.856
				while (wps.MoveNext())
				{
					#region Print WebPage content
					//using (FileStream f = File.Open(@"C:\users\federico\desktop\records\page" + k + "_content.txt", FileMode.Create))
					//{
					//	using (MemoryMappedViewStream stream = wps.Current.GetContent())
					//		stream.CopyTo(f);
					//}
					#endregion
					#region Print WebPage signature
					//using (StreamWriter f = new StreamWriter(WORKING_DIRECTORY + "/signatures/page" + k + ".txt"))
					//{
					//	f.Write(wps.Current.StartPage);
					//	f.Write('\n');
					//	f.Write(wps.Current.EndPage);
					//	f.Write('\n');
					//	if (wps.Current.Signature != null)
					//	{
					//		foreach (ulong sig in wps.Current.Signature)
					//		{
					//			f.Write(sig);
					//			f.Write('\n');
					//		}
					//	}
					//}
					#endregion

					if (wps.Current.Signature != null)
					{
						lsh.AddDocument(wps.Current);
						categWebPages.Add(wps.Current);
					}
					else
						unCategWebPages.Add(wps.Current);

					webpages.Add(wps.Current);
					k++;
				}

				Console.WriteLine("|\tPages processed: " + (k - 1) + ".");
				Console.WriteLine("|\tTime: " + (int)stopWatch.Elapsed.TotalSeconds + " seconds.");
				Console.WriteLine("END FIRST PHASE.");

				#endregion

				#region SECOND PHASE

				Console.WriteLine("SECOND PHASE: WRITING INTO THE OUPUT FILE...");
				stopWatch.Restart();

				// Compute the new order of the pages
				StreamWriter log = new StreamWriter(WORKING_DIRECTORY + "PageOrder.txt", false);
				long offset = 0;
				while (categWebPages.Count > 0)
				{
					foreach (WebPage wp in lsh.UnionFind(categWebPages.First()).OrderBy(wp => wp.URL).ToList())
					{
						ComputePrintOrder(wp, ref offset, log);
						categWebPages.Remove(wp);
					}
					log.Write('\n');
				}
				categWebPages = null;

				foreach (WebPage wp in unCategWebPages.OrderBy(wp => wp.URL).ToList())
					ComputePrintOrder(wp, ref offset, log);
				unCategWebPages = null;
				log.Write('\n');
				log.Close();

				// Write the result
				long sizeInput = new FileInfo(WORKING_DIRECTORY + "Input.warc").Length;
				MemoryMappedFile output = MemoryMappedFile.CreateFromFile(WORKING_DIRECTORY + "Output.warc", FileMode.Create, "OUTPUT", sizeInput);
				foreach (WebPage wp in webpages)
				{
					using (MemoryMappedViewStream outputStream = output.CreateViewStream(wp.NewStartPage, wp.Length))
						using (MemoryMappedViewStream inputStream = wp.GetContent())
							inputStream.CopyTo(outputStream);
				}
				output.Dispose();

				Console.WriteLine("|\tTime: " + (int)stopWatch.Elapsed.TotalSeconds + " seconds.");
				Console.WriteLine("END SECOND PHASE.");
				stopWatch.Stop();

				#endregion
				
				sf.Dispose();
			}
			catch (IOException) { Console.WriteLine("File " + WORKING_DIRECTORY + "Input.warc do not exist!"); }
			catch (ArgumentOutOfRangeException) { Console.WriteLine("System's logical address space capacity exceeded!"); }
			catch (UnauthorizedAccessException) { Console.WriteLine("Cannot access a memory area outside the memory mapped file!"); }
			
			Console.ReadLine();
		}
	}
}