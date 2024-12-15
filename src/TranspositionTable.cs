using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Puffin.Constants;

namespace Puffin
{
   public enum HashFlag : byte
   {
      None,
      Exact,
      Alpha,
      Beta
   }

   public struct TTEntry(ulong hash, byte depth, ushort move, HashFlag flag, int score)
   {
      public ulong Hash { get; private set; } = hash; // 8 bytes
      public int Score { get; private set; } = score; // 4 bytes
      public ushort Move { get; private set; } = move; // 2 bytes
      public byte Depth { get; private set; } = depth; // 1 byte
      public HashFlag Flag { get; private set; } = flag; // 1 byte

      public void Update(ulong hash, byte depth, ushort move, int score, HashFlag flag)
      {
         if (move != 0 || hash != Hash)
         {
            Move = move;
         }

         Hash = hash;
         Depth = depth;
         Score = score;
         Flag = flag;
      }
   }

   public struct TranspositionTable()
   {
      TTEntry[] Table = new TTEntry[32 * 1024 * 1024 / 16]; // Default to 32MB table size

      // Size in MB
      public void Resize(int size)
      {
         // Note that the Array.Resize method doesn't actually resize. It creates a copy of the original with the new size,
         // and then updates the memory pointer.
         Array.Resize(ref Table, size * 1024 * 1024 / 16);
         Array.Clear(Table);
      }

      public readonly void Reset()
      {
         Array.Clear(Table);
      }

      public readonly bool Probe(ulong hash, int ply, out TTEntry entry)
      {
         ref TTEntry current = ref GetEntry(hash);

         if (current.Hash != hash)
         {
            entry = default;
            return false;
         }

         int adjustedScore = current.Score;

         // Mate score adjustments
         if (adjustedScore > MATE - MAX_PLY)
         {
            adjustedScore -= ply;
         }
         else if (adjustedScore < -(MATE - MAX_PLY))
         {
            adjustedScore += ply;
         }

         entry = new(current.Hash, current.Depth, current.Move, current.Flag, adjustedScore);

         return true;
      }

      public readonly void SaveEntry(ulong hash, byte depth, int ply, ushort move, int score, HashFlag flag)
      {
         ref TTEntry entry = ref GetEntry(hash);

         // Mate score adjustments
         if (score > MATE - MAX_PLY)
         {
            score += ply;
         }
         else if (score < -(MATE - MAX_PLY))
         {
            score -= ply;
         }

         entry.Update(hash, depth, move, score, flag);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private readonly ref TTEntry GetEntry(ulong hash)
      {
         return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Table), (int)(hash % (ulong)Table.Length));
      }

      /// <summary>
      /// Returns an estimate of the number of entries in the table
      /// </summary>
      /// <returns></returns>
      public readonly int GetUsed()
      {
         int used = 0;

         for (int i = 0; i < 1000; i++)
         {
            if (Table[i].Hash != 0)
            {
               used++;
            }
         }

         return used;
      }
   }
}
