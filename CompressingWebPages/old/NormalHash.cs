using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompressingWebPages
{
	class MurmurHash
	{
		ulong a = 0, b = 0, p = 9945341325618271301;

		#region CONSTRUCTOR
		public MurmurHash()
		{
			Random rnd = new Random();
			a = ((ulong)rnd.Next() << 32) + (ulong)rnd.Next();
			b = ((ulong)rnd.Next() << 32) + (ulong)rnd.Next();
		}
		#endregion

		#region ComputeHash
		public ulong ComputeHash(ulong value)
		{
			return (value * a + b) % p;
		}
		#endregion

		#region ComputeHash2
		#endregion
	}
}
