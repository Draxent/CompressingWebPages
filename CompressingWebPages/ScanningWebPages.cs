using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.MemoryMappedFiles;

namespace CompressingWebPages
{
	class ScanningWebPages : IDisposable
	{
		private MemoryMappedFile mmf;
		private MemoryMappedViewAccessor accessor;
		private int accessorIndex;
		private List<long> posChunkWebPages;
		public byte[] Container;
		public byte[] PermutedContainer;
		
		#region CONSTRUCTOR
		public ScanningWebPages(String nameFile, List<long> posChunkWebPages)
		{
			this.mmf = MemoryMappedFile.CreateFromFile(nameFile, FileMode.Open);
			this.accessor = this.mmf.CreateViewAccessor(posChunkWebPages[0], posChunkWebPages[1] - posChunkWebPages[0] + 1);
			this.accessorIndex = 0;
			this.posChunkWebPages = posChunkWebPages;
			this.Container = new byte[1];
			this.PermutedContainer = new byte[1];
		}
		#endregion

		#region WebPageContent
		public void WebPageContent(int i, long start, int length, int marginSpace, int num = 1)
		{
			if (length > this.Container.Length)
			{
				this.Container = new byte[length + marginSpace];
				this.PermutedContainer = new byte[length + marginSpace];
			}

			int q = i / GlobalVars.SIZE_CHUNK_WEBPAGES;
			int r = i % GlobalVars.SIZE_CHUNK_WEBPAGES;
			if (r + num > GlobalVars.SIZE_CHUNK_WEBPAGES)
				throw new InvalidOperationException();

			if (q != this.accessorIndex)
			{
				this.accessor.Dispose();
				this.accessor = this.mmf.CreateViewAccessor(posChunkWebPages[q], posChunkWebPages[q + 1] - posChunkWebPages[q] + 1);
				this.accessorIndex = q;
			}

			this.accessor.ReadArray(start - posChunkWebPages[q], this.Container, 0, length);
		}
		#endregion

		#region Dispose
		public void Dispose()
		{
			this.Container = null;
			this.PermutedContainer = null;
			this.accessor.Dispose();
			this.mmf.Dispose();
		}
		#endregion
	}
}
