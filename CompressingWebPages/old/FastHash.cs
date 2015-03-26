using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompressingWebPages
{
	class FastHash
	{
		private uint seed;

		#region CONSTRUCTOR
		public FastHash(uint seed)
		{
			this.seed = seed;
		}
		#endregion

		public static ulong Hash(ulong key)
		{
			byte[] dataToHash = new byte[8];
			dataToHash[0] = (byte)key;
			dataToHash[1] = (byte)(key >> 8);
			dataToHash[2] = (byte)(key >> 16);
			dataToHash[3] = (byte)(key >> 24);
			dataToHash[4] = (byte)(key >> 32);
			dataToHash[5] = (byte)(key >> 40);
			dataToHash[6] = (byte)(key >> 48);
			dataToHash[7] = (byte)(key >> 56);

			ulong hash = (ulong)(dataToHash[0] | dataToHash[1] << 8 | dataToHash[2] << 16 | dataToHash[3] << 32);
			ulong tmp = (ulong)((ulong)(dataToHash[4] | dataToHash[5] << 8 | dataToHash[6] << 16 | dataToHash[7] << 32) << 22) ^ hash;
			hash = (hash << 32) ^ tmp;
			hash += hash >> 22;

			return hash;
		}
	}
}
