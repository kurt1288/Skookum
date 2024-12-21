using System.Runtime.CompilerServices;

namespace Puffin
{
   internal ref struct MoveList(Span<(Move, int)> moves)
   {
      private readonly Span<(Move Move, int Score)> Moves = moves;

      public int Count { get; private set; } = 0;

      public readonly ref readonly Move this[int index]
      {
         get => ref Moves[index].Move;
      }

      public readonly void Shuffle(Random rnd)
      {
         rnd.Shuffle(Moves);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly int GetScore(int index) => Moves[index].Score;
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly void SetScore(int index, int score) => Moves[index].Score = score;
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public void Add(Move move) => Moves[Count++] = (move, 0);
      public readonly void Add(Move move, int score, int index) => Moves[index] = (move, score);

      public void RemoveAt(int index)
      {
         Moves.Slice(index + 1, Moves.Length - index - 1).CopyTo(Moves[index..]);
         Count--;
      }

      public void Clear()
      {
         Count = 0;
      }

      /// <summary>
      /// NEVER USE THIS. ONLY FOR DATAGEN
      /// </summary>
      public void Reset()
      {
         Moves.Clear();
         Count = 0;
      }

      public readonly void SwapMoves(int index1, int index2)
      {
         var temp = Moves[index1];
         Moves[index1] = Moves[index2];
         Moves[index2] = temp;
      }
   }
}
