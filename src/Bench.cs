using System.Diagnostics;

namespace Puffin
{
   internal class Bench(int depth)
   {
      public void Run()
      {
         TimeManager Timer = new();
         Timer.ConfigureDepth(depth);

         long totalNodes = 0;
         double totalMs = 0;
         string[] lines = System.IO.File.ReadAllLines("./fens.txt");
         Board board = new();
         TranspositionTable table = new();
         SearchInfo searchInfo = new();
         Stopwatch sw = new();

         foreach (string line in lines)
         {
            board.Reset();
            table.Reset();
            searchInfo.ResetAll();

            board.SetPosition(line);
            sw.Restart();
            Search search = new(board, Timer, ref table, searchInfo);
            search.Run();
            totalNodes += Search.GetTotalNodes();
            totalMs += sw.ElapsedMilliseconds;
         }

         Console.WriteLine($"Total time: {totalMs / 1000} seconds");
         Console.WriteLine($"Total nodes: {totalNodes:N0}");
      }
   }
}
