using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompressingWebPages
{
	class FastMurmurHash
	{
		const ulong m = 0xc6a4a7935bd1e995;
		const int r = 47;
		private uint seed;

		public FastMurmurHash(uint seed)
		{
			this.seed = seed;
		}

		#region Hash
		public ulong Hash(ulong key)
		{
			ulong h = this.seed ^ m;

			key *= m;
			key ^= key >> r;
			key *= m;

			h ^= key;
			h *= m;

			h ^= h >> r;
			h *= m;
			h ^= h >> r;

			return h;
		}
		#endregion
	}
}
