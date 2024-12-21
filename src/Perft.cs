using System.Diagnostics;

namespace Puffin
{
   internal class Perft(Board board)
   {
      private readonly Board Board = board;

      public ulong Run(int depth)
      {
         if (depth == 0)
         {
            return 0;
         }

         Stopwatch stopWatch = new();
         stopWatch.Start();

         ulong totalNodes = 0;
         Span<(Move, int)> moveBuffer = stackalloc (Move, int)[218];
         MoveList moves = new(moveBuffer);
         MoveGen.GenerateAll(Board, ref moves);

         for (int i = 0; i < moves.Count; i++)
         {
            Move move = moves[i];

            if (!Board.MakeMove(move))
            {
               Board.UndoMove(move);
               continue;
            }

            ulong childNodes = 0;
            childNodes += Divide(depth - 1);

            Console.WriteLine($"{move}: {childNodes}");

            totalNodes += childNodes;

            Board.UndoMove(move);
         }

         stopWatch.Stop();

         TimeSpan ts = stopWatch.Elapsed;

         Console.WriteLine($"{Environment.NewLine}Nodes searched: {totalNodes.ToString("N0")}");
         Console.WriteLine($"Elapsed time: {Math.Round(ts.TotalMilliseconds / 1000, 5)} seconds");
         Console.WriteLine($"NPS: {Math.Round(totalNodes / (ts.TotalMilliseconds / 1000)).ToString("N0")}");

         return totalNodes;
      }

      ulong Divide(int depth)
      {
         ulong nodes = 0;

         if (depth == 0)
         {
            return 1;
         }

         Span<(Move, int)> moveBuffer = stackalloc (Move, int)[218];
         MoveList moves = new(moveBuffer);
         MoveGen.GenerateAll(Board, ref moves);

         for (int i = 0; i < moves.Count; i++)
         {
            Move move = moves[i];

            if (!Board.MakeMove(move))
            {
               Board.UndoMove(move);
               continue;
            }

            nodes += Divide(depth - 1);

            Board.UndoMove(move);
         }

         return nodes;
      }
   }
}
