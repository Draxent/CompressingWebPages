using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.MemoryMappedFiles;

namespace CompressingWebPages
{	
	class ScanningFile : IDisposable
	{
		// Size of the file segmentation expressed in 2^exp Byte -> 256 MB
		private const int EXP = 28;
		// We have set up the buffersize to 40 KB (average size of a web page)
		private const int BUFFERSIZE = 40960;

		// The mapped file.
		private MemoryMappedFile mmf;
		// Array of hashed permutations precalculated.
		private FastMurmurHash[] permutations;
		// The chunk of file retrieved and its index.
		private IEnumerator<Tuple<long, MemoryMappedViewAccessor>> chunks;
		// Saves in a list the last MemoryMappedViewAccessor.
		private MemoryMappedViewAccessor lastChunk = null;
		// The last bytes read are saved into the buffer.
		private byte[] buffer = new byte[BUFFERSIZE];
		private long lastIndexRead = long.MaxValue;
		// Sketch Vector Size.
		public int SketchVectorSize;

		#region CONSTRUCTOR
		public ScanningFile(String nameFile, FastMurmurHash[] permutations, int sketchVectorSize)
		{
			FileInfo info = new FileInfo(nameFile);
			string mapName = info.Name.Split(new char[1] { '.' })[0].ToUpper();

			this.mmf = MemoryMappedFile.CreateFromFile(nameFile, FileMode.Open, mapName);
			this.permutations = permutations;
			this.SketchVectorSize = sketchVectorSize;
			this.chunks = this.FileSegmentation(info.Length);
			this.chunks.MoveNext();

			for (int i = 0; i < BUFFERSIZE; i++)
				buffer[i] = 0;
		}
		#endregion

		#region FileSegmentation
		private IEnumerator<Tuple<long, MemoryMappedViewAccessor>> FileSegmentation(long sizeFile)
		{
			long sizeChunk = 1 << EXP;
			long numChunks = sizeFile >> EXP;
			long lastChunkSize = sizeFile & (sizeChunk - 1);
			long offset = 0;

			for (long i = 0; i < numChunks; i++, offset += sizeChunk)
				yield return new Tuple<long, MemoryMappedViewAccessor>(offset, this.mmf.CreateViewAccessor(offset, sizeChunk));

			if (lastChunkSize > 0)
				yield return new Tuple<long, MemoryMappedViewAccessor>(offset, this.mmf.CreateViewAccessor(offset, lastChunkSize));
		}
		#endregion

		#region ReadFile
		public MemoryMappedViewStream ReadFile(long start, long size)
		{
			return this.mmf.CreateViewStream(start, size);
		}
		#endregion

		#region Get
		private byte GetCurrent(long index)
		{
			int s = (int)(chunks.Current.Item2.Capacity - index);
			if (s > 0)
			{
				long diff = index - lastIndexRead;
				if (diff < 0 || diff >= BUFFERSIZE || diff >= s)
				{
					diff = 0;
					lastIndexRead = index;
					if (s < BUFFERSIZE)
						chunks.Current.Item2.ReadArray(index, buffer, 0, s);
					else
						chunks.Current.Item2.ReadArray(index, buffer, 0, BUFFERSIZE);
				}
				return buffer[diff];
			}
			else
				throw new IndexOutOfRangeException();
		}
		public long AbsoluteLocation(long index)
		{
			long sizeChunk = 1 << EXP;
			int q = (int)(index >> EXP);
			long r = index & ((1 << EXP) - 1);

			if (lastChunk != null)
			{
				if (q == 0) return chunks.Current.Item1 - sizeChunk + r;
				else if (q == 1) return chunks.Current.Item1 + r;
				else
					throw new InvalidOperationException("Cannot load in memory more than one chunk!");
			}
			else
			{
				if (q == 0) return chunks.Current.Item1 + r;
				else if (q == 1)
				{
					// Saves the current chunk
					lastChunk = chunks.Current.Item2;
					// Retrieves the next chunk
					bool canMove = chunks.MoveNext();
					if (!canMove) // Reached EOF
						throw new IndexOutOfRangeException();

					return chunks.Current.Item1 + r;
				}
				else
					throw new InvalidOperationException("Cannot load in memory more than one chunk!");
			}
		}
		public byte Get(long index)
		{
			int q = (int)(index >> EXP);
			long r = index & ((1 << EXP) - 1);

			if (lastChunk != null)
			{
				if (q == 0) return lastChunk.ReadByte(r);
				else if (q == 1) return this.GetCurrent(r);
				else
					throw new InvalidOperationException("Cannot load in memory more than one chunk!");
			}
			else
			{
				if (q == 0) return this.GetCurrent(r);
				else if (q == 1)
				{
					// Save the current chunk
					lastChunk = chunks.Current.Item2;
					// Retrieves the next chunk
					bool canMove = chunks.MoveNext();
					if (!canMove) // Reached EOF
						throw new IndexOutOfRangeException();

					return this.GetCurrent(r);
				}
				else
					throw new InvalidOperationException("Cannot load in memory more than one chunk!");
			}
		}
		public char GetChar(long i)
		{
			return (char)this.Get(i);
		}
		#endregion

		#region EOF
		public bool EOF(long i)
		{
			try { this.Get(i); return false; }
			catch (IndexOutOfRangeException) { return true; }
		}
		#endregion

		#region Match
		public bool Match(long i, byte b)
		{
			return (this.Get(i) == b);
		}
		#endregion

		#region MatchBytes
		public bool MatchBytes(long i, byte[] bb)
		{
			foreach (byte b in bb)
				if (!this.Match(i++, b)) return false;
			return true;
		}
		public bool MatchBytes(long i, byte[] bb1, byte[] bb2)
		{
			bool matched = this.MatchBytes(i, bb1);
			if (!matched)
				matched = this.MatchBytes(i, bb2);
			return matched;
		}
		#endregion

		#region Free
		public long Free(long index)
		{
			int q = (int)(index >> EXP);
			long r = index & ((1 << EXP) - 1);

			if ((q > 1) || (lastChunk == null && q == 1))
				throw new InvalidOperationException();

			if (lastChunk != null && q == 1)
			{
				lastChunk.Dispose();
				lastChunk = null;
				return r;
			}
			else return index;
		}
		#endregion

		#region Copy
		public string Copy(long start, long end)
		{
			long length = end - start + 1;
			string content = "";
			for (long i = 0; i < length; i++)
				content += this.GetChar(i + start);
			return content;
		}
		#endregion

		#region Search
		public Tuple<bool, long> Search(byte patternByte, long offset = 0, byte[] stopPattern = null, long maxWindow = 0)
		{
			long i = -1;
			// the search looks for maximum sizeChunk bytes
			if (maxWindow == 0)
				maxWindow = 1 << EXP;

			try
			{
				for (i = offset; i < offset + maxWindow; i++)
				{
					byte current = this.Get(i);
					if (current == patternByte) return new Tuple<bool, long>(true, i);
					else if (stopPattern != null)
					{
						bool found = true;
						for (int j = 0; j < stopPattern.Length; j++)
							if (this.Get(i + j) != stopPattern[j]) { found = false; break; }
						if (found) return new Tuple<bool, long>(false, i);
					}
				}
			}
			catch (IndexOutOfRangeException) { }

			// Return the last i found, useful to get the last byte processed by the Search function
			return new Tuple<bool, long>(false, i);
		}
		public Tuple<bool, long> Search(byte[][] patterns, int n, long offset = 0, byte[] stopPattern = null, long maxWindow = 0)
		{
			long i = -1;
			// the search looks for maximum sizeChunk bytes
			if (maxWindow == 0)
				maxWindow = 1 << EXP;

			try
			{
				for (i = offset; i < offset + maxWindow; i++)
				{
					char c = this.GetChar(i);
					foreach (byte[] p in patterns)
					{
						bool found = true;
						// Searches the pattern
						for (int j = 0; j < p.Length; j++)
							if (this.Get(i + j) != p[j]) { found = false; break; }
						if (found) return new Tuple<bool, long>(true, i);
					}

					if (stopPattern != null)
					{
						bool found = true;
						for (int j = 0; j < stopPattern.Length; j++)
							if (this.Get(i + j) != stopPattern[j]) { found = false; break; }
						if (found) return new Tuple<bool, long>(false, i);
					}
				}
			}
			catch (IndexOutOfRangeException) { }
				
			// Return the last i found, useful to get the last byte processed by the Search function
			return new Tuple<bool, long>(false, i);
		}
		#endregion

		#region WebPageSegmentation
		public IEnumerator<WebPage> WebPageSegmentation()
		{
			WebPage wp = null;
			uint idPage = 1;
			long endLastPage = 0;

			while (true)
			{
				try { wp = new WebPage(idPage, this, this.permutations, endLastPage); }
				catch (EndOfStreamException) { break; }

				yield return wp;

				// If we reach this point should mean that we have already used the discovered WebPage, so we can free the buffer.
				endLastPage = this.Free(wp.GetRelativeEndPage());
				idPage++;
			}
		}
		#endregion

		#region Dispose
		public void Dispose()
		{
			this.mmf.Dispose();
		}
		#endregion
	}
}
