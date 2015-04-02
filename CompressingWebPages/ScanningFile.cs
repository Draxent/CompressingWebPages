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

		// The mapped file.
		private MemoryMappedFile mmf;
		// The chunk of file retrieved and its index.
		private IEnumerator<Tuple<long, MemoryMappedViewAccessor>> chunks;
		// Saves in a list the last MemoryMappedViewAccessor.
		private MemoryMappedViewAccessor lastChunk = null;
		// The last bytes read are saved into the buffer.
		private byte[] buffer;
		private long lastIndexRead = long.MaxValue;

		#region CONSTRUCTOR
		public ScanningFile(String nameFile, long sizeFile)
		{
			this.mmf = MemoryMappedFile.CreateFromFile(nameFile, FileMode.Open);
			this.chunks = this.FileSegmentation(sizeFile);
			this.chunks.MoveNext();

			this.buffer = new byte[1024]; // 1KB
			for (int i = 0; i < this.buffer.Length; i++)
				this.buffer[i] = 0;
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

		#region GetFromBuffer
		private byte GetFromBuffer(MemoryMappedViewAccessor chunk, long index)
		{
			int s = (int)(chunk.Capacity - index);
			if (s > 0)
			{
				long diff = index - this.lastIndexRead;
				int l = Math.Min(s, this.buffer.Length);
				if (diff < 0 || diff >= l)
				{
					diff = 0;
					this.lastIndexRead = index;
					chunk.ReadArray(index, this.buffer, 0, l);
				}
				return this.buffer[diff];
			}
			else
				throw new IndexOutOfRangeException();
		}
		#endregion

		#region Get
		public byte Get(long index)
		{
			int q = (int)(index >> EXP);
			long r = index & ((1 << EXP) - 1);

			if (lastChunk != null)
			{
				if (q == 0) return this.GetFromBuffer(this.lastChunk, r);
				else if (q == 1) return this.GetFromBuffer(this.chunks.Current.Item2, r);
				else
					throw new InvalidOperationException("Cannot load in memory more than one chunk!");
			}
			else
			{
				if (q == 0) return this.GetFromBuffer(this.chunks.Current.Item2, r);
				else if (q == 1)
				{
					// Save the current chunk
					lastChunk = chunks.Current.Item2;
					// Retrieves the next chunk
					bool canMove = chunks.MoveNext();
					if (!canMove) // Reached EOF
						throw new IndexOutOfRangeException();

					return this.GetFromBuffer(this.chunks.Current.Item2, r);
				}
				else
					throw new InvalidOperationException("Cannot load in memory more than one chunk!");
			}
		}
		#endregion

		#region AbsoluteLocation
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
		#endregion

		#region Free
		public long Free(long index)
		{
			int q = (int)(index >> EXP);
			long r = index & ((1 << EXP) - 1);

			if (q > 1) throw new InvalidOperationException();

			if (lastChunk != null && q == 1)
			{
				// Reset lastIndexRead if it was set inside the lastChunk
				int q2 = (int)(lastIndexRead >> EXP);
				if (q2 == 0)
					this.lastIndexRead = long.MaxValue;
				// Remove lastChunk
				this.lastChunk.Dispose();
				this.lastChunk = null;
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
				content += (char)this.Get(i + start);
			return content;
		}
		#endregion

		#region Search
		public Tuple<bool, long> Search(byte patternByte, long offset = 0, long maxWindow = -1)
		{
			long i = -1;
			// the search looks for maximum sizeChunk bytes
			if (maxWindow == -1)
				maxWindow = 1 << EXP;

			try
			{
				for (i = offset; i < offset + maxWindow; i++)
				{
					byte current = this.Get(i);
					if (current == patternByte) return new Tuple<bool, long>(true, i);
				}
			}
			catch (IndexOutOfRangeException) { }

			// Return the last i found, useful to get the last byte processed by the Search function
			return new Tuple<bool, long>(false, i);
		}
		public Tuple<bool, long> Search(byte[][] patterns, long offset = 0, long maxWindow = -1)
		{
			long i = -1;
			// the search looks for maximum sizeChunk bytes
			if (maxWindow == -1)
				maxWindow = 1 << EXP;

			try
			{
				for (i = offset; i < offset + maxWindow; i++)
				{
					foreach (byte[] p in patterns)
					{
						bool found = true;
						// Searches the pattern
						for (int j = 0; j < p.Length; j++)
							if (this.Get(i + j) != p[j]) { found = false; break; }
						if (found) return new Tuple<bool, long>(true, i);
					}
				}
			}
			catch (IndexOutOfRangeException) { }
				
			// Return the last i found, useful to get the last byte processed by the Search function
			return new Tuple<bool, long>(false, i);
		}
		#endregion

		#region WebPageSegmentation
		public IEnumerator<ProcessWebPage> WebPageSegmentation()
		{
			ProcessWebPage p = null;
			int idPage = 0;
			long endLastPage = 0;

			while (true)
			{
				try { p = new ProcessWebPage(idPage, this, endLastPage); }
				catch (EndOfStreamException) { break; }

				yield return p;

				// If we reach this point should mean that we have already used the discovered WebPage, so we can free the buffer.
				endLastPage = this.Free(endLastPage + p.WebPage.Length);
				idPage++;
			}
		}
		#endregion

		#region Dispose
		public void Dispose()
		{
			this.buffer = null;
			if (lastChunk != null) lastChunk.Dispose();
			this.chunks.Current.Item2.Dispose();
			this.mmf.Dispose();
		}
		#endregion
	}
}
