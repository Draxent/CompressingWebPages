using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace CompressingWebPages
{
	class LSH
	{
		private int sketchSize;
		private int numPick;
		private int numIter;
		private int[][] drawings;
		private Dictionary<ulong, List<WebPage>>[] buckets;

		#region CONSTRUCTOR
		public LSH(int sketchSize, int numIter, int numPick)
		{
			Random rnd = new Random();
			this.sketchSize = sketchSize;
			this.numIter = numIter;
			this.numPick = numPick;
			this.drawings = new int[numIter][];
			this.buckets = new Dictionary<ulong, List<WebPage>>[numIter];

			for (int i = 0; i < numIter; i++)
			{
				HashSet<int> set = new HashSet<int>();
				this.drawings[i] = new int[numPick];
				this.buckets[i] = new Dictionary<ulong, List<WebPage>>();

				while (set.Count < this.numPick)
					set.Add(rnd.Next(sketchSize - 1));
				this.drawings[i] = set.ToArray();
			}
		}
		#endregion

		#region HashFunction
		private static ulong HashFunction(int numPick, ulong[] signature, int[] drawings)
		{
			ulong sum = 0;
			for (int j = 0; j < numPick; j++)
				sum += signature[drawings[j]];
			return sum;
		}
		#endregion

		#region AddDocument
		public void AddDocument(WebPage wp)
		{
			if (wp.Signature == null)
				throw new ArgumentNullException();

			for (int i = 0; i < this.numIter; i++)
			{
				ulong v = HashFunction(this.numPick, wp.Signature, this.drawings[i]);

				if (this.buckets[i].ContainsKey(v))
					this.buckets[i][v].Add(wp);
				else
				{
					List<WebPage> l = new List<WebPage>();
					l.Add(wp);
					this.buckets[i].Add(v, l);
				}
			}
		}
		#endregion

		#region FindSimilar
		private void FindSimilar(HashSet<WebPage> similarDoc, WebPage wp)
		{
			if (wp.Signature == null)
				throw new ArgumentNullException();

			for (int i = 0; i < this.numIter; i++)
			{
				ulong v = HashFunction(this.numPick, wp.Signature, this.drawings[i]);

				if (!this.buckets[i].ContainsKey(v))
					throw new InvalidOperationException();

				foreach (WebPage sim in this.buckets[i][v])
					similarDoc.Add(sim);
			}
		}
		#endregion

		#region UnionFind
		public HashSet<WebPage> UnionFind(WebPage wp)
		{
			HashSet<WebPage> similarDoc = new HashSet<WebPage>();
			HashSet<WebPage> visited = new HashSet<WebPage>();

			FindSimilar(similarDoc, wp);
			visited.Add(wp);

			while (similarDoc.Count != visited.Count)
			{
				WebPage sim = similarDoc.First(p => !visited.Contains(p));
				FindSimilar(similarDoc, sim);
				visited.Add(sim);
			}

			return similarDoc;
		}
		#endregion
	}
}
