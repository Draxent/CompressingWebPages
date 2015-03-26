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

		public ulong Hash(ulong key)
		{
			byte[] data = new byte[8];
			data[0] = (byte)key;
			data[1] = (byte)(key >> 8);
			data[2] = (byte)(key >> 16);
			data[3] = (byte)(key >> 24);
			data[4] = (byte)(key >> 32);
			data[5] = (byte)(key >> 40);
			data[6] = (byte)(key >> 48);
			data[7] = (byte)(key >> 56);

			ulong h = this.seed ^ 8;
			ulong k = (ulong)(data[0] | data[1] << 8 | data[2] << 16 | data[3] << 32);

			k *= m;
			k ^= k >> r;
			k *= m;

			h ^= k;
			h *= m;

			h ^= h >> r;
			h *= m;
			h ^= h >> r;

			return h;
		}
	}
}
