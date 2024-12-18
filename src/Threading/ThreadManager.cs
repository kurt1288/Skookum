using System.Collections.Concurrent;

namespace Puffin
{
   internal class ThreadManager
   {
      private readonly Thread[] Threads;
      private readonly SearchInfo[] Infos;
      private readonly BlockingCollection<Search> SearchQueue;
      private volatile bool IsRunning = true;
      private TranspositionTable tTable;
      private readonly int ThreadCount;

      public ThreadManager(int threadCount, ref TranspositionTable tTable)
      {
         ThreadCount = threadCount;
         Threads = new Thread[threadCount];
         Infos = new SearchInfo[threadCount];
         SearchQueue = [];
         this.tTable = tTable;

         for (int i = 0; i < threadCount; i++)
         {
            Infos[i] = new SearchInfo();
            int threadIndex = i;

            Threads[i] = new Thread(ThreadWork)
            {
               IsBackground = true,
               Name = $"Thread {i}"
            };

            Threads[i].Start();
         }
      }

      private void ThreadWork()
      {
         while (IsRunning)
         {
            if (SearchQueue.TryTake(out Search task, Timeout.Infinite))
            {
               task.Run();
            }
         }
      }

      public void StartSearches(TimeManager time, Board board)
      {
         time.Start();

         for (int i = 0; i < ThreadCount; i++)
         {
            SearchQueue.Add(new((Board)board.Clone(), time, ref tTable, Infos[i]));
         }
      }

      public void Shutdown()
      {
         IsRunning = false;
         SearchQueue.CompleteAdding();

         foreach (Thread thread in Threads)
         {
            thread.Join();
         }
      }

      public void Reset()
      {
         for (int i = 0; i < ThreadCount; i++)
         {
            Infos[i].ResetAll();
         }
      }
   }
}
