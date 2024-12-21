using static Puffin.Constants;
using static Puffin.Attacks.Attacks;
using System.Diagnostics;

namespace Puffin
{
   internal sealed class MoveGen
   {
      public static void GenerateAll(Board board, ref MoveList list)
      {
         GenerateQuiet(ref list, board);
         GenerateNoisy(ref list, board);
      }

      public static void GenerateQuiet(ref MoveList moveList, Board board)
      {
         GeneratePawnPushes(ref moveList, board);
         GenerateCastling(ref moveList, board);

         ulong occupied = board.ColorBoard(Color.Both).Value;
         Bitboard nonPawns = board.ColorBoard(board.SideToMove) & ~board.PieceBoard((int)PieceType.Pawn).Value;

         while (nonPawns)
         {
            int from = nonPawns.GetLSB();
            nonPawns.ClearLSB();

            Bitboard quiets = board.Squares[from].Type switch
            {
               PieceType.Knight => new(KnightAttacks[from]),
               PieceType.Bishop => new(GetBishopAttacks(from, occupied)),
               PieceType.Rook => new(GetRookAttacks(from, occupied)),
               PieceType.Queen => new(GetQueenAttacks(from, occupied)),
               PieceType.King => new(KingAttacks[from]),
               _ => throw new Exception($"Unable to get moves for piece {board.Squares[from].Type}"),
            };

            quiets &= ~occupied;
            while (quiets)
            {
               moveList.Add(new Move(from, quiets.GetLSB(), MoveFlag.Quiet));
               quiets.ClearLSB();
            }
         }
      }

      public static void GenerateNoisy(ref MoveList moveList, Board board)
      {
         GeneratePawnAttacks(ref moveList, board);
         GenerateEnPassant(ref moveList, board);
         GeneratePawnPromotions(ref moveList, board);

         ulong occupied = board.ColorBoard(Color.Both).Value;
         Bitboard nonPawns = board.ColorBoard(board.SideToMove) & ~board.PieceBoard((int)PieceType.Pawn).Value;

         while (nonPawns)
         {
            int from = nonPawns.GetLSB();
            nonPawns.ClearLSB();

            Bitboard attacks = board.Squares[from].Type switch
            {
               PieceType.Knight => new(KnightAttacks[from]),
               PieceType.Bishop => new(GetBishopAttacks(from, occupied)),
               PieceType.Rook => new(GetRookAttacks(from, occupied)),
               PieceType.Queen => new(GetQueenAttacks(from, occupied)),
               PieceType.King => new(KingAttacks[from]),
               _ => throw new Exception($"Unable to get attacks for piece {board.Squares[from].Type}"),
            };

            attacks &= board.ColorBoard(board.SideToMove ^ (Color)1);
            while (attacks)
            {
               moveList.Add(new Move(from, attacks.GetLSB(), MoveFlag.Capture));
               attacks.ClearLSB();
            }
         }
      }

      // Only generates QUIET pawn moves (no promotions, attacks, en passant, etc.)
      public static void GeneratePawnPushes(ref MoveList moveList, Board board)
      {
         ulong pawns = board.ColorPieceBB(board.SideToMove, PieceType.Pawn).Value;
         ulong empty = ~board.ColorBoard(Color.Both).Value;
         int up = board.SideToMove == Color.White ? -8 : 8;
         int startRank = board.SideToMove == Color.White ? 6 : 1;
         Bitboard targets = board.SideToMove == Color.White ? new(pawns >> 8) : new(pawns << 8);
         targets &= empty & ~RANK_MASKS[board.SideToMove == Color.White ? (int)Rank.Rank_8 : (int)Rank.Rank_1];

         while (targets)
         {
            int square = targets.GetLSB();
            targets.ClearLSB();
            moveList.Add(new Move(square - up, square, MoveFlag.Quiet));

            if (square - up >> 3 == startRank && (SquareBB[square + up] & empty) != 0)
            {
               moveList.Add(new Move(square - up, square + up, MoveFlag.DoublePawnPush));
            }
         }
      }

      public static void GeneratePawnAttacks(ref MoveList moveList, Board board)
      {
         ulong pawns = board.ColorPieceBB(board.SideToMove, PieceType.Pawn).Value;
         Bitboard rightTargets = new((pawns & ~FILE_MASKS[(int)File.H]) >> 7);
         Bitboard leftTargets = new((pawns & ~FILE_MASKS[(int)File.A]) >> 9);
         int upRight = board.SideToMove == Color.White ? -7 : 7;
         int upLeft = board.SideToMove == Color.White ? -9 : 9;

         if (board.SideToMove == Color.Black)
         {
            rightTargets = new((pawns & ~FILE_MASKS[(int)File.A]) << 7);
            leftTargets = new((pawns & ~FILE_MASKS[(int)File.H]) << 9);
         }

         rightTargets &= board.ColorBoard(board.SideToMove ^ (Color)1);
         leftTargets &= board.ColorBoard(board.SideToMove ^ (Color)1);

         while (rightTargets)
         {
            int to = rightTargets.GetLSB();
            rightTargets.ClearLSB();
            int from = to - upRight;

            if (to / 8 == (int)Rank.Rank_8 || to / 8 == (int)Rank.Rank_1)
            {
               // Pawn attack to promotion
               moveList.Add(new Move(from, to, MoveFlag.KnightPromotionCapture));
               moveList.Add(new Move(from, to, MoveFlag.BishopPromotionCapture));
               moveList.Add(new Move(from, to, MoveFlag.RookPromotionCapture));
               moveList.Add(new Move(from, to, MoveFlag.QueenPromotionCapture));
            }
            else
            {
               moveList.Add(new Move(from, to, MoveFlag.Capture));
            }
         }

         while (leftTargets)
         {
            int to = leftTargets.GetLSB();
            leftTargets.ClearLSB();
            int from = to - upLeft;

            if (to / 8 == (int)Rank.Rank_8 || to / 8 == (int)Rank.Rank_1)
            {
               // Pawn attack to promotion
               moveList.Add(new Move(from, to, MoveFlag.KnightPromotionCapture));
               moveList.Add(new Move(from, to, MoveFlag.BishopPromotionCapture));
               moveList.Add(new Move(from, to, MoveFlag.RookPromotionCapture));
               moveList.Add(new Move(from, to, MoveFlag.QueenPromotionCapture));
            }
            else
            {
               moveList.Add(new Move(from, to, MoveFlag.Capture));
            }
         }
      }

      // Pawn pushes to promotions (no attacks)
      public static void GeneratePawnPromotions(ref MoveList moveList, Board board)
      {
         ulong pawns = board.ColorPieceBB(board.SideToMove, PieceType.Pawn).Value;
         ulong empty = ~board.ColorBoard(Color.Both).Value;
         Bitboard targets = new(pawns >> 8 & RANK_MASKS[(int)Rank.Rank_8]);
         int up = board.SideToMove == Color.White ? -8 : 8;

         if (board.SideToMove == Color.Black)
         {
            targets = new(pawns << 8 & RANK_MASKS[(int)Rank.Rank_1]);
         }

         targets &= empty;

         while (targets)
         {
            int square = targets.GetLSB();
            targets.ClearLSB();
            moveList.Add(new Move(square - up, square, MoveFlag.KnightPromotion));
            moveList.Add(new Move(square - up, square, MoveFlag.BishopPromotion));
            moveList.Add(new Move(square - up, square, MoveFlag.RookPromotion));
            moveList.Add(new Move(square - up, square, MoveFlag.QueenPromotion));
         }
      }

      public static void GenerateEnPassant(ref MoveList moveList, Board board)
      {
         Bitboard attackers = board.EnPassant != Square.Null
            ? new(PawnAttacks[(int)board.SideToMove ^ 1][(int)board.EnPassant]
               & board.ColorPieceBB(board.SideToMove, PieceType.Pawn).Value)
            : new();

         while (attackers)
         {
            int square = attackers.GetLSB();
            attackers.ClearLSB();
            moveList.Add(new Move(square, (int)board.EnPassant, MoveFlag.EPCapture));
         }
      }

      public static void GenerateCastling(ref MoveList moveList, Board board)
      {
         Bitboard rookSquares = new(board.CastleSquares & (board.SideToMove == Color.White ? RANK_MASKS[(int)Rank.Rank_1] : RANK_MASKS[(int)Rank.Rank_8]));

         if (board.InCheck || !rookSquares)
         {
            return;
         }

         int kingSquare = board.SideToMove == Color.White ? board.PieceBoard(PieceType.King).GetMSB() : board.PieceBoard(PieceType.King).GetLSB();
         ulong occupied = board.ColorBoard(Color.Both).Value;
         int kingCastleSquare = board.SideToMove == Color.White ? (int)Square.G1 : (int)Square.G8;
         int queenCastleSquare = board.SideToMove == Color.White ? (int)Square.C1 : (int)Square.C8;
         int oppositeColor = (int)board.SideToMove ^ 1;

         while (rookSquares)
         {
            int rookSquare = rookSquares.GetLSB();
            rookSquares.ClearLSB();

            if ((BetweenBB[kingSquare][rookSquare] & occupied) != 0)
            {
               continue;
            }

            int targetSquare = (kingSquare < rookSquare) ? kingCastleSquare : queenCastleSquare;
            int pathSquare = (kingSquare < rookSquare) ? targetSquare - 1 : targetSquare + 1;

            // Check the path of the king to make sure it isn't moving through check
            if (!board.IsAttacked(pathSquare, oppositeColor))
            {
               moveList.Add(new Move(
                  kingSquare,
                  targetSquare,
                  kingSquare < rookSquare ? MoveFlag.KingCastle : MoveFlag.QueenCastle
               ));
            }
         }
      }
   }
}
