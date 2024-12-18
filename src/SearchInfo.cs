using System.Runtime.CompilerServices;
using static Puffin.Constants;

namespace Puffin
{
   internal class SearchInfo
   {
      private Move[][] Pv = new Move[MAX_PLY][];
      private int[] PvLength = new int[MAX_PLY];
      private readonly Move[] CounterMoves = new Move[64 * 64];
      private readonly int[] ContinuationHistory = new int[12 * 64 * 12 * 64]; // [prev piece * prev to square * curr piece * curr to square]
      private readonly int[] QuietHistory = new int[2 * 64 * 64]; // [side to move * from square * to square]

      public int[] EvalStack { get; set; } = new int[MAX_PLY]; 
      public Move[][] KillerMoves { get; set; } = new Move[MAX_PLY][];
      public int Score { get; set; } = -INFINITY;

      public SearchInfo()
      {
         for (int i = 0; i < MAX_PLY; i++)
         {
            Pv[i] = new Move[MAX_PLY];
            KillerMoves[i] = new Move[2];
         }
      }

      public void ResetAll()
      {
         Array.Clear(QuietHistory);
         Array.Clear(ContinuationHistory);
         Array.Clear(PvLength);
         Array.Clear(CounterMoves);
         Array.Clear(EvalStack);
         Score = -INFINITY;

         for (int i = 0; i < MAX_PLY; i++)
         {
            Pv[i] = new Move[MAX_PLY];
            KillerMoves[i] = new Move[2];
         }
      }

      public void ResetForSearch()
      {
         Array.Clear(PvLength);
         Score = -INFINITY;

         for (int i = 0; i < MAX_PLY; i++)
         {
            Pv[i] = new Move[MAX_PLY];
            KillerMoves[i] = new Move[2];
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void InitPvLength(int ply)
      {
         PvLength[ply] = ply;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public Move GetBestMove()
      {
         return Pv[0][0];
      }

      public string GetPv()
      {
         string pv = "";

         for (int i = 0; i < PvLength[0]; i++)
         {
            pv += Pv[0][i] + " ";
         }

         return pv;
      }

      public void UpdatePV(Move move, int ply)
      {
         Pv[ply][ply] = move;
         for (int i = ply + 1; i < PvLength[ply + 1]; i++)
         {
            Pv[ply][i] = Pv[ply + 1][i];
         }
         PvLength[ply] = PvLength[ply + 1];
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void UpdateCountermove(Move previousMove, Move currentMove)
      {
         CounterMoves[previousMove.From * 64 + previousMove.To] = currentMove;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public Move GetCountermove(Move previousMove)
      {
         return CounterMoves[previousMove.From * 64 + previousMove.To];
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void UpdateHistory(Color color, Move move, int value)
      {
         AddToHistory(ref QuietHistory[(int)color * 4096 + move.From * 64 + move.To], value, 5000);
      }
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public int GetHistory(Color color, Move move)
      {
         return QuietHistory[(int)color * 4096 + move.From * 64 + move.To];
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void UpdateContHistory(Piece currPiece, Move currMove, (Move Move, Piece Piece)[] moveStack, int ply, int offset, int value)
      {
         if (ply > offset)
         {
            int currIndex = ((int)currPiece.Color * 6 * 64) + ((int)currPiece.Type * 64) + currMove.To;
            (Move Move, Piece Piece) prev = moveStack[ply - offset];
            int prevIndex = ((int)prev.Piece.Color * 6 * 64) + ((int)prev.Piece.Type * 64) + prev.Move.To;

            AddToHistory(ref ContinuationHistory[(prevIndex * 12 * 64) + currIndex], value, 15000);
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public int GetContHistory(Piece currPiece, Move currMove, (Move Move, Piece Piece)[] moveStack, int ply, int offset)
      {
         if (ply > offset)
         {
            int currIndex = ((int)currPiece.Color * 6 * 64) + ((int)currPiece.Type * 64) + currMove.To;
            (Move Move, Piece Piece) prev = moveStack[ply - offset];
            int prevIndex = ((int)prev.Piece.Color * 6 * 64) + ((int)prev.Piece.Type * 64) + prev.Move.To;

            return ContinuationHistory[(prevIndex * 12 * 64) + currIndex];
         }

         return 0;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static void AddToHistory(ref int history, int value, int maxValue)
      {
         history += value - history * Math.Abs(value) / maxValue;
      }
   }
}
