using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.MemoryMappedFiles;


namespace CompressingWebPages
{
	class LSH
	{
		// LSH Threshold t = (1/b)^(1/r), that in this case, where b = 7 and r = 3, the threshold t = 52%
		public const int NUMITER = 7; // b
		public const int NUMPICK = 3; // r

		private int[][] drawings;
		private Dictionary<ulong, List<int>>[] buckets;

		#region CONSTRUCTOR
		public LSH()
		{
			Random rnd = new Random();
			this.drawings = new int[NUMITER][];
			this.buckets = new Dictionary<ulong, List<int>>[NUMITER];

			for (int i = 0; i < NUMITER; i++)
			{
				HashSet<int> set = new HashSet<int>();
				this.drawings[i] = new int[NUMPICK];
				this.buckets[i] = new Dictionary<ulong, List<int>>();

				while (set.Count < NUMPICK)
					set.Add(rnd.Next(GlobalVars.SKETCH_VECTOR_SIZE - 1));
				this.drawings[i] = set.ToArray();
			}
		}
		#endregion

		#region HashFunction
		private static ulong HashFunction(ulong[] signature, int[] drawings)
		{
			ulong sum = 0;
			for (int j = 0; j < NUMPICK; j++)
				sum += signature[drawings[j]];
			return sum;
		}
		#endregion

		#region AddDocument
		public void AddDocument(int id, ulong[] signature)
		{
			if (signature == null)
				throw new ArgumentNullException();

			for (int i = 0; i < NUMITER; i++)
			{
				ulong v = HashFunction(signature, this.drawings[i]);

				if (this.buckets[i].ContainsKey(v))
					this.buckets[i][v].Add(id);
				else
				{
					List<int> l = new List<int>();
					l.Add(id);
					this.buckets[i].Add(v, l);
				}
			}
		}
		#endregion

		#region FindSimilar
		private void FindSimilar(HashSet<int> similarDoc, int id, ulong[] signature, HashSet<int>[] skip)
		{
			if (signature == null)
				throw new ArgumentNullException();

			for (int i = 0; i < NUMITER; i++)
			{
				if (skip[i].Contains(id))
					continue;

				ulong v = HashFunction(signature, this.drawings[i]);

				if (!this.buckets[i].ContainsKey(v))
					throw new InvalidOperationException();

				foreach (int sim in this.buckets[i][v])
				{
					similarDoc.Add(sim);
					skip[i].Add(sim);
				}
			}
		}
		#endregion

		#region UnionFind
		public HashSet<int> UnionFind(List<WebPage> webpages, int id)
		{
			HashSet<int> similarDoc = new HashSet<int>();
			HashSet<int> visited = new HashSet<int>();
			HashSet<int>[] skip = new HashSet<int>[NUMITER];
			for (int i = 0; i < NUMITER; i++)
				skip[i] = new HashSet<int>();

			this.FindSimilar(similarDoc, id, webpages[id].Signature, skip);
			visited.Add(id);

			while (similarDoc.Count != visited.Count)
			{
				int sim = similarDoc.First(p => !visited.Contains(p));

				this.FindSimilar(similarDoc, sim, webpages[sim].Signature, skip);
				visited.Add(sim);
			}

			return similarDoc;
		}
		#endregion
	}
}
