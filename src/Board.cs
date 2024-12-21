using System.Diagnostics;
using System.Runtime.CompilerServices;
using static Puffin.Constants;
using static Puffin.Attacks.Attacks;

namespace Puffin
{
   internal sealed class Board : ICloneable
   {
      private ulong UniqueHash = 0;
      private readonly Bitboard[] ColorBB = new Bitboard[3];
      private readonly Bitboard[] PieceBB = new Bitboard[6];

      public Piece[] Squares { get; } = new Piece[64];
      public Color SideToMove { get; private set; } = Color.Null;
      public Square EnPassant { get; private set; } = Square.Null;
      public ulong CastleSquares { get; private set; } = 0;
      public int Halfmoves { get; private set; } = 0;
      public int Phase { get; private set; } = 0;
      public History History { get; } = new();
      public Score[] MaterialScore { get; } = [new(0, 0), new(0, 0)];
      public (Move Move, Piece Piece)[] MoveStack { get; private set; } = new (Move Move, Piece Piece)[1000]; // arbitrary 1000 moves max

      public ulong Hash => UniqueHash;
      public bool InCheck => IsAttacked(GetSquareByPiece(PieceType.King, SideToMove), (int)SideToMove ^ 1);

      public Bitboard ColorPieceBB(Color color, PieceType piece) => ColorBB[(int)color] & PieceBB[(int)piece];
      public Bitboard ColorBoard(Color color) => ColorBB[(int)color];
      public Bitboard PieceBoard(PieceType piece) => PieceBB[(int)piece];
      public Bitboard NonPawnMaterial => PieceBB[(int)PieceType.Knight] | PieceBB[(int)PieceType.Bishop] | PieceBB[(int)PieceType.Rook] | PieceBB[(int)PieceType.Queen];

      public Board()
      {
         for (int i = 0; i < Squares.Length; i++)
         {
            Squares[i] = Piece.Null;
         }

         for (int i = 0; i < ColorBB.Length; i++)
         {
            ColorBB[i].Reset();
         }

         for (int i = 0; i < PieceBB.Length; i++)
         {
            PieceBB[i].Reset();
         }
      }

      public Board(Board other)
      {
         SideToMove = other.SideToMove;
         EnPassant = other.EnPassant;
         CastleSquares = other.CastleSquares;
         Halfmoves = other.Halfmoves;
         Phase = other.Phase;
         UniqueHash = other.Hash;
         Array.Copy(other.Squares, Squares, Squares.Length);
         Array.Copy(other.ColorBB, ColorBB, ColorBB.Length);
         Array.Copy(other.PieceBB, PieceBB, PieceBB.Length);
         Array.Copy(other.MaterialScore, MaterialScore, MaterialScore.Length);
      }

      public object Clone()
      {
         return new Board(this);
      }

      public void Reset()
      {
         for (int i = 0; i < Squares.Length; i++)
         {
            Squares[i] = Piece.Null;
         }

         for (int i = 0; i < ColorBB.Length; i++)
         {
            ColorBB[i].Reset();
         }

         for (int i = 0; i < PieceBB.Length; i++)
         {
            PieceBB[i].Reset();
         }

         History.Reset();
         SideToMove = Color.Null;
         EnPassant = Square.Null;
         CastleSquares = 0;
         Halfmoves = 0;
         Phase = 0;
         MaterialScore[(int)Color.White] = new Score(0, 0);
         MaterialScore[(int)Color.Black] = new Score(0, 0);
         UniqueHash = 0;
      }

      public void SetPosition(string fen)
      {
         Reset();

         try
         {
            string[] fenParts = fen.Split();
            string pieces = fenParts[0];

            int square = 0;

            foreach (char piece in pieces)
            {
               if (char.IsNumber(piece))
               {
                  square += (int)char.GetNumericValue(piece);
               }
               else
               {
                  if (piece == '/')
                  {
                     continue;
                  }

                  SetPiece(new Piece(piece), square);
                  square++;
               }
            }

            SideToMove = fenParts[1] == "w" ? Color.White : Color.Black;

            // castling
            foreach (char letter in fenParts[2])
            {
               switch (letter)
               {
                  case 'K':
                     {
                        CastleSquares |= PieceBB[(int)PieceType.Rook].Value & ColorBB[(int)Color.White].Value & RANK_MASKS[(int)Rank.Rank_1] & FILE_MASKS[(int)File.H];
                        break;
                     }
                  case 'Q':
                     {
                        CastleSquares |= PieceBB[(int)PieceType.Rook].Value & ColorBB[(int)Color.White].Value & RANK_MASKS[(int)Rank.Rank_1] & FILE_MASKS[(int)File.A];
                        break;
                     }
                  case 'k':
                     {
                        CastleSquares |= PieceBB[(int)PieceType.Rook].Value & ColorBB[(int)Color.Black].Value & RANK_MASKS[(int)Rank.Rank_8] & FILE_MASKS[(int)File.H];
                        break;
                     }
                  case 'q':
                     {
                        CastleSquares |= PieceBB[(int)PieceType.Rook].Value & ColorBB[(int)Color.Black].Value & RANK_MASKS[(int)Rank.Rank_8] & FILE_MASKS[(int)File.A];
                        break;
                     }
                  default:
                     {
                        break;
                     }
               }
            }

            // set en passant
            if (fenParts[3] != "-")
            {
               var f = fenParts[3][0];
               int file = "abcdefgh".IndexOf(fenParts[3][0]);
               int rank = 8 - int.Parse(fenParts[3][1].ToString());
               EnPassant = (Square)(rank * 8 + file);
            }

            if (fenParts.Length > 4 && int.TryParse(fenParts[4], out int halfMoves))
            {
               Halfmoves = halfMoves;
            }

            UniqueHash = Zobrist.GenerateHash(this);
         }
         catch
         {
            Console.WriteLine($"Unable to parse fen");
         }
      }

      // Returns false if the move is illegal (leaves the king in check)
      public bool MakeMove(Move move)
      {
         MoveFlag flag = move.Flag;
         int from = move.From;
         int to = move.To;
         Piece piece = Squares[from];

         History.Add(EnPassant, CastleSquares, Squares[flag == MoveFlag.EPCapture ? piece.Color == Color.White ? to + 8 : to - 8 : to], Halfmoves, UniqueHash, Phase);

         if (EnPassant != Square.Null)
         {
            Zobrist.UpdateEnPassant(ref UniqueHash, EnPassant);
         }

         EnPassant = Square.Null;
         Halfmoves += 1;

         if (piece.Type == PieceType.Pawn || move.HasType(MoveType.Capture))
         {
            Halfmoves = 0;
         }

         RemovePiece(piece, from);

         switch (flag)
         {
            case MoveFlag.Quiet:
               {
                  SetPiece(piece, to);
                  break;
               }
            case MoveFlag.DoublePawnPush:
               {
                  SetPiece(piece, to);
                  EnPassant = (Square)((to + from) / 2);
                  Zobrist.UpdateEnPassant(ref UniqueHash, EnPassant);
                  break;
               }
            case MoveFlag.Capture:
               {
                  RemovePiece(Squares[to], to);
                  SetPiece(piece, to);
                  break;
               }
            case MoveFlag.EPCapture:
               {
                  SetPiece(piece, to);
                  RemovePiece(new Piece(PieceType.Pawn, (Color)((int)piece.Color ^ 1)), piece.Color == Color.White ? to + 8 : to - 8);
                  break;
               }
            case MoveFlag.KingCastle:
               {
                  // Move king
                  SetPiece(piece, to);

                  // Move rook
                  int rFrom = new Bitboard(CastleSquares & RANK_MASKS[SideToMove == Color.White ? (int)Rank.Rank_1 : (int)Rank.Rank_8]).GetMSB();
                  int rTo = SideToMove == Color.White ? (int)Square.F1 : (int)Square.F8;
                  SetPiece(Squares[rFrom], rTo);
                  RemovePiece(Squares[rFrom], rFrom);
                  break;
               }
            case MoveFlag.QueenCastle:
               {
                  // Move king
                  SetPiece(piece, to);

                  // Move rook
                  int rFrom = new Bitboard(CastleSquares & RANK_MASKS[SideToMove == Color.White ? (int)Rank.Rank_1 : (int)Rank.Rank_8]).GetLSB();
                  int rTo = SideToMove == Color.White ? (int)Square.D1 : (int)Square.D8;
                  SetPiece(Squares[rFrom], rTo);
                  RemovePiece(Squares[rFrom], rFrom);
                  break;
               }
            case MoveFlag.KnightPromotion:
               {
                  SetPiece(new Piece(PieceType.Knight, piece.Color), to);
                  break;
               }
            case MoveFlag.BishopPromotion:
               {
                  SetPiece(new Piece(PieceType.Bishop, piece.Color), to);
                  break;
               }
            case MoveFlag.RookPromotion:
               {
                  SetPiece(new Piece(PieceType.Rook, piece.Color), to);
                  break;
               }
            case MoveFlag.QueenPromotion:
               {
                  SetPiece(new Piece(PieceType.Queen, piece.Color), to);
                  break;
               }
            case MoveFlag.KnightPromotionCapture:
               {
                  RemovePiece(Squares[to], to);
                  SetPiece(new Piece(PieceType.Knight, piece.Color), to);
                  break;
               }
            case MoveFlag.BishopPromotionCapture:
               {
                  RemovePiece(Squares[to], to);
                  SetPiece(new Piece(PieceType.Bishop, piece.Color), to);
                  break;
               }
            case MoveFlag.RookPromotionCapture:
               {
                  RemovePiece(Squares[to], to);
                  SetPiece(new Piece(PieceType.Rook, piece.Color), to);
                  break;
               }
            case MoveFlag.QueenPromotionCapture:
               {
                  RemovePiece(Squares[to], to);
                  SetPiece(new Piece(PieceType.Queen, piece.Color), to);
                  break;
               }
            default:
               {
                  throw new Exception($"Unable to make move: {move}. Unknown flag: {flag}");
               }
         }

         // update castling
         ulong homeRank = RANK_MASKS[SideToMove == Color.White ? (int)Rank.Rank_1 : (int)Rank.Rank_8];
         ulong affectedSquares = SquareBB[move.From] | SquareBB[move.To];
         ulong castleRightsToRemove = 0;

         // Update castling rights if the king moves
         if (piece.Type == PieceType.King && (CastleSquares & homeRank) != 0)
         {
            castleRightsToRemove |= CastleSquares & homeRank;
         }

         // Update castling rights if a rook moves or is captured
         castleRightsToRemove |= CastleSquares & affectedSquares;

         // Apply the updates
         if (castleRightsToRemove != 0)
         {
            Zobrist.UpdateCastle(ref UniqueHash, castleRightsToRemove);
            CastleSquares &= ~castleRightsToRemove;
         }

         SideToMove ^= (Color)1;

         Zobrist.UpdateSideToMove(ref UniqueHash);

         Debug.Assert(Zobrist.Verify(UniqueHash, this));
         Debug.Assert(Phase == VerifyPhase());
         Debug.Assert(MaterialScore[0] == Evaluation.Material(this, Color.White));
         Debug.Assert(MaterialScore[1] == Evaluation.Material(this, Color.Black));

         return !IsAttacked(GetSquareByPiece(PieceType.King, SideToMove ^ (Color)1), (int)SideToMove);
      }

      public void MakeNullMove()
      {
         History.Add(EnPassant, CastleSquares, Piece.Null, Halfmoves, Hash, Phase);

         if (EnPassant != Square.Null)
         {
            Zobrist.UpdateEnPassant(ref UniqueHash, EnPassant);
         }

         EnPassant = Square.Null;
         Halfmoves = 0;
         SideToMove ^= (Color)1;

         Zobrist.UpdateSideToMove(ref UniqueHash);

         Debug.Assert(Zobrist.Verify(UniqueHash, this));
         Debug.Assert(Phase == VerifyPhase());
      }

      public void UndoMove(Move move)
      {
         ref readonly BoardState previousState = ref History.Pop();

         SideToMove ^= (Color)1;
         EnPassant = previousState.En_Passant;
         CastleSquares = previousState.CastleSquares;
         Halfmoves = previousState.Halfmoves;

         int from = move.From;
         int to = move.To;
         Piece piece = Squares[to];

         RemovePiece(piece, to);

         if (move.HasType(MoveType.Promotion))
         {
            SetPiece(new Piece(PieceType.Pawn, piece.Color), from);
         }
         else if (move.IsCastle())
         {
            SetPiece(piece, from);

            Piece rook = new(PieceType.Rook, piece.Color);

            if (move.Flag == MoveFlag.KingCastle)
            {
               RemovePiece(rook, piece.Color == Color.White ? (int)Square.F1 : (int)Square.F8);
               SetPiece(rook, piece.Color == Color.White ? (int)Square.H1 : (int)Square.H8);
            }
            else
            {
               RemovePiece(rook, piece.Color == Color.White ? (int)Square.D1 : (int)Square.D8);
               SetPiece(rook, piece.Color == Color.White ? (int)Square.A1 : (int)Square.A8);
            }
         }
         else
         {
            SetPiece(piece, from);
         }

         if (move.HasType(MoveType.Capture))
         {
            if (move.Flag == MoveFlag.EPCapture)
            {
               SetPiece(previousState.CapturedPiece, piece.Color == Color.White ? to + 8 : to - 8);
            }
            else
            {
               SetPiece(previousState.CapturedPiece, to);
            }
         }

         Phase = previousState.Phase;
         UniqueHash = previousState.Hash;
         Debug.Assert(Zobrist.Verify(Hash, this));
         Debug.Assert(Phase == VerifyPhase());
         Debug.Assert(MaterialScore[0] == Evaluation.Material(this, Color.White));
         Debug.Assert(MaterialScore[1] == Evaluation.Material(this, Color.Black));
      }

      public void UnmakeNullMove()
      {
         ref readonly BoardState previousState = ref History.Pop();

         SideToMove ^= (Color)1;
         EnPassant = previousState.En_Passant;
         CastleSquares = previousState.CastleSquares;
         Halfmoves = previousState.Halfmoves;
         Phase = previousState.Phase;
         UniqueHash = previousState.Hash;

         Debug.Assert(Zobrist.Verify(UniqueHash, this));
         Debug.Assert(Phase == VerifyPhase());
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private void SetPiece(Piece piece, int square)
      {
         ColorBB[(int)piece.Color].SetBit(square);
         PieceBB[(int)piece.Type].SetBit(square);
         ColorBB[(int)Color.Both].SetBit(square);
         Squares[square] = piece;
         Phase += PHASE_VALUES[(int)piece.Type];
         Zobrist.UpdatePieces(ref UniqueHash, piece, square);
         MaterialScore[(int)piece.Color] += Evaluation.PieceValues[(int)piece.Type];
         MaterialScore[(int)piece.Color] += Evaluation.GetPSTScore(piece, square);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private void RemovePiece(Piece piece, int square)
      {
         ColorBB[(int)piece.Color].ResetBit(square);
         PieceBB[(int)piece.Type].ResetBit(square);
         ColorBB[(int)Color.Both].ResetBit(square);
         Squares[square] = Piece.Null;
         Phase -= PHASE_VALUES[(int)piece.Type];
         Zobrist.UpdatePieces(ref UniqueHash, piece, square);
         MaterialScore[(int)piece.Color] -= Evaluation.PieceValues[(int)piece.Type];
         MaterialScore[(int)piece.Color] -= Evaluation.GetPSTScore(piece, square);
      }

      public int GetSquareByPiece(PieceType piece, Color color)
      {
         return new Bitboard(PieceBB[(int)piece].Value & ColorBB[(int)color].Value).GetLSB();
      }

      public bool IsAttacked(int square, int color)
      {
         if ((PawnAttacks[color ^ 1][square] & PieceBB[(int)PieceType.Pawn].Value & ColorBB[color].Value) != 0)
         {
            return true;
         }

         if ((KnightAttacks[square] & PieceBB[(int)PieceType.Knight].Value & ColorBB[color].Value) != 0)
         {
            return true;
         }

         if ((KingAttacks[square] & PieceBB[(int)PieceType.King].Value & ColorBB[color].Value) != 0)
         {
            return true;
         }

         ulong occupied = ColorBB[(int)Color.White].Value | ColorBB[(int)Color.Black].Value;
         ulong bishopQueens = (PieceBB[(int)PieceType.Bishop].Value | PieceBB[(int)PieceType.Queen].Value) & ColorBB[color].Value;

         if ((GetBishopAttacks(square, occupied) & bishopQueens) != 0)
         {
            return true;
         }

         ulong rookQueens = (PieceBB[(int)PieceType.Rook].Value | PieceBB[(int)PieceType.Queen].Value) & ColorBB[color].Value;

         if ((GetRookAttacks(square, occupied) & rookQueens) != 0)
         {
            return true;
         }

         return false;
      }

      public ulong AttackersTo(int square, ulong occupied)
      {
         return (PawnAttacks[(int)Color.Black][square] & PieceBB[(int)PieceType.Pawn].Value & ColorBB[(int)Color.White].Value)
            | (PawnAttacks[(int)Color.White][square] & PieceBB[(int)PieceType.Pawn].Value & ColorBB[(int)Color.Black].Value)
            | (KnightAttacks[square] & PieceBB[(int)PieceType.Knight].Value)
            | (GetBishopAttacks(square, occupied) & (PieceBB[(int)PieceType.Bishop].Value | PieceBB[(int)PieceType.Queen].Value))
            | (GetRookAttacks(square, occupied) & (PieceBB[(int)PieceType.Rook].Value | PieceBB[(int)PieceType.Queen].Value))
            | (KingAttacks[square] & PieceBB[(int)PieceType.King].Value);
      }

      // Determines if there is a draw by insufficient material
      public bool IsDrawn()
      {
         // KvK
         if ((ColorBB[(int)Color.White] | ColorBB[(int)Color.Black]).CountBits() == 2)
         {
            return true;
         }

         // Board with pawn, rook, or queen(s) can't be material draw
         if (PieceBB[(int)PieceType.Pawn] | PieceBB[(int)PieceType.Rook] | PieceBB[(int)PieceType.Queen])
         {
            return false;
         }

         // KvB or KvN
         if ((PieceBB[(int)PieceType.Knight] | PieceBB[(int)PieceType.Bishop]).CountBits() <= 1)
         {
            return true;
         }

         // KvNN
         if (!PieceBB[(int)PieceType.Bishop] && PieceBB[(int)PieceType.Knight].CountBits() == 2)
         {
            return true;
         }

         return false;
      }

      public bool IsWon()
      {
         // One side has only a king left
         if (ColorBB[(int)Color.White].Value == (PieceBB[(int)PieceType.King].Value & ColorBB[(int)Color.White].Value))
         {
            // Other side only has a king and either a rook or queen
            if (ColorBB[(int)Color.Black].Value == ((PieceBB[(int)PieceType.King].Value | PieceBB[(int)PieceType.Queen].Value) & ColorBB[(int)Color.Black].Value))
            {
               return true;
            }
            else if (ColorBB[(int)Color.Black].Value == ((PieceBB[(int)PieceType.King].Value | PieceBB[(int)PieceType.Rook].Value) & ColorBB[(int)Color.Black].Value))
            {
               return true;
            }
            // 2 bishops, on different color squares?
            else if (ColorBB[(int)Color.Black].Value == ((PieceBB[(int)PieceType.King].Value | PieceBB[(int)PieceType.Bishop].Value) & ColorBB[(int)Color.Black].Value)
               && PieceBB[(int)PieceType.Bishop].CountBits() == 2)
            {
               int sq1 = PieceBB[(int)PieceType.Bishop].GetLSB();
               int sq2 = PieceBB[(int)PieceType.Bishop].GetMSB();

               return ((9 * (sq1 ^ sq2)) & 8) != 0;
            }
            // bishop + knight
            else if (ColorBB[(int)Color.Black].Value == ((PieceBB[(int)PieceType.King].Value | PieceBB[(int)PieceType.Bishop].Value | PieceBB[(int)PieceType.Knight].Value) & ColorBB[(int)Color.Black].Value))
            {               
               if (PieceBB[(int)PieceType.Knight].CountBits() == 1 && PieceBB[(int)PieceType.Bishop].CountBits() == 1)
               {
                  return true;
               }

               return false;
            }
         }
         else if (ColorBB[(int)Color.Black].Value == (PieceBB[(int)PieceType.King].Value & ColorBB[(int)Color.Black].Value))
         {
            if (ColorBB[(int)Color.White].Value == ((PieceBB[(int)PieceType.King].Value | PieceBB[(int)PieceType.Queen].Value) & ColorBB[(int)Color.White].Value))
            {
               return true;
            }
            else if (ColorBB[(int)Color.White].Value == ((PieceBB[(int)PieceType.King].Value | PieceBB[(int)PieceType.Rook].Value) & ColorBB[(int)Color.White].Value))
            {
               return true;
            }
            else if (ColorBB[(int)Color.White].Value == ((PieceBB[(int)PieceType.King].Value | PieceBB[(int)PieceType.Bishop].Value) & ColorBB[(int)Color.White].Value)
               && PieceBB[(int)PieceType.Bishop].CountBits() == 2)
            {
               int sq1 = PieceBB[(int)PieceType.Bishop].GetLSB();
               int sq2 = PieceBB[(int)PieceType.Bishop].GetMSB();

               return ((9 * (sq1 ^ sq2)) & 8) != 0;
            }
            else if (ColorBB[(int)Color.White].Value == ((PieceBB[(int)PieceType.King].Value | PieceBB[(int)PieceType.Bishop].Value | PieceBB[(int)PieceType.Knight].Value) & ColorBB[(int)Color.White].Value))
            {
               if (PieceBB[(int)PieceType.Knight].CountBits() == 1 && PieceBB[(int)PieceType.Bishop].CountBits() == 1)
               {
                  return true;
               }

               return false;
            }
         }

         return false;
      }

      public bool IsThreefold()
      {
         if (Halfmoves < 4 || History.Count <= 1)
         {
            return false;
         }

         int last = Math.Max(History.Count - Halfmoves, 0);

         for (int i = History.Count - 4; i >= last; i -= 2)
         {
            if (History[i].Hash == Hash)
            {
               return true;
            }
         }

         return false;
      }

      public bool IsPseudoLegal(Move move)
      {
         // Null move?
         // No piece on the from square?
         // Piece on the from square isn't the correct color?
         if (move == 0 || Squares[move.From].Type == PieceType.Null || Squares[move.From].Color != SideToMove)
         {
            return false;
         }

         Piece piece = Squares[move.From];
         int up = (piece.Color == Color.White) ? -8 : 8;

         // there's no piece to move?
         // moving an opponent's piece?
         // trying to capture a king
         if (piece.Type == PieceType.Null || piece.Color != SideToMove || Squares[move.To].Type == PieceType.King)
         {
            return false;
         }

         // trying to capture our own piece in a non-castle move?
         if (Squares[move.To].Type != PieceType.Null && Squares[move.To].Color == SideToMove && !move.IsCastle())
         {
            return false;
         }

         if (piece.Type == PieceType.Pawn)
         {
            if (move.Flag == MoveFlag.EPCapture && EnPassant != Square.Null && Squares[move.To - up].Type == PieceType.Pawn && Squares[move.To - up].Color != piece.Color)
            {
               return true;
            }

            // moving backwards
            if (piece.Color == Color.White ? (move.To >> 3) > (move.From >> 3) : (move.To >> 3) < (move.From >> 3))
            {
               return false;
            }

            // move to backrank that isn't a promotion
            if (!move.HasType(MoveType.Promotion) && (move.To >> 3) == (piece.Color == Color.White ? (int)Rank.Rank_1 : (int)Rank.Rank_8))
            {
               return false;
            }

            if (move.HasType(MoveType.Capture))
            {
               return Squares[move.To].Type != PieceType.Null && (PawnAttacks[(int)piece.Color][move.From] & SquareBB[move.To]) != 0;
            }

            // Non-captures should remain on the same file
            if ((move.From & 7) != (move.To & 7))
            {
               return false;
            }

            // double push
            if ((move.From ^ move.To) == 16)
            {
               // Can't double push from a non-starting rank
               if (move.From >> 3 != (piece.Color == Color.White ? (int)Rank.Rank_7 : (int)Rank.Rank_2) || move.Flag != MoveFlag.DoublePawnPush)
               {
                  return false;
               }

               return Squares[move.To].Type == PieceType.Null && Squares[move.To - up].Type == PieceType.Null;
            }
            // single push or attack
            else
            {
               // Can't push a pawn more than 1 rank at this point
               if (Math.Abs((move.From >> 3) - (move.To >> 3)) > 1)
               {
                  return false;
               }

               return Squares[move.To].Type == PieceType.Null;
            }
         }

         if (move.IsCastle())
         {
            if (piece.Type != PieceType.King || InCheck)
            {
               return false;
            }

            int homeRank = piece.Color == Color.White ? 7 : 0;
            if (move.To >> 3 != homeRank || move.From >> 3 != homeRank)
            {
               return false;
            }

            bool isKingSide = move.Flag == MoveFlag.KingCastle;
            int castleSquare = isKingSide ?
                (piece.Color == Color.White ? (int)Square.H1 : (int)Square.H8) :
                (piece.Color == Color.White ? (int)Square.A1 : (int)Square.A8);

            if ((CastleSquares & SquareBB[castleSquare]) == 0)
            {
               return false;
            }

            ulong path = BetweenBB[move.From][castleSquare];
            if ((path & (ColorBB[(int)Color.White].Value | ColorBB[(int)Color.Black].Value)) != 0)
            {
               return false;
            }

            int pathSquare = isKingSide ?
                (piece.Color == Color.White ? (int)Square.G1 : (int)Square.G8) - 1 :
                (piece.Color == Color.White ? (int)Square.C1 : (int)Square.C8) + 1;

#if DEBUG
            Span<(Move, int)> buffer = stackalloc (Move, int)[218];
            MoveList castleMoves = new(buffer);
            MoveGen.GenerateCastling(ref castleMoves, this);
            bool foundCastle = false;
            for (int i = 0; i < castleMoves.Count; i++)
            {
               if (castleMoves[i] == move)
               {
                  foundCastle = true;
               }
            }

            Debug.Assert(foundCastle == !IsAttacked(pathSquare, (int)piece.Color ^ 1));
#endif

            return !IsAttacked(pathSquare, (int)piece.Color ^ 1);
         }

         // At this point, pawn moves, quiet moves to an occupied space, and capture moves to an unoccupied space are all invalid
         if (move.HasType(MoveType.Promotion) || move.Flag == MoveFlag.DoublePawnPush || move.Flag == MoveFlag.EPCapture
            || (move.Flag == MoveFlag.Quiet && Squares[move.To].Type != PieceType.Null)
            || (move.Flag == MoveFlag.Capture && Squares[move.To].Type == PieceType.Null))
         {
            return false;
         }

         Bitboard moves = Squares[move.From].Type switch
         {
            PieceType.Knight => new(KnightAttacks[move.From]),
            PieceType.Bishop => new(GetBishopAttacks(move.From, ColorBB[(int)Color.White].Value | ColorBB[(int)Color.Black].Value)),
            PieceType.Rook => new(GetRookAttacks(move.From, ColorBB[(int)Color.White].Value | ColorBB[(int)Color.Black].Value)),
            PieceType.Queen => new(GetQueenAttacks(move.From, ColorBB[(int)Color.White].Value | ColorBB[(int)Color.Black].Value)),
            PieceType.King => new(KingAttacks[move.From]),
            _ => throw new Exception($"Unable to get attacks for piece {Squares[move.From].Type}"),
         };

#if DEBUG
         Span<(Move, int)> moveBuffer = stackalloc (Move, int)[218];
         MoveList moveGen = new(moveBuffer);
         MoveGen.GenerateAll(this, ref moveGen);
         bool found = false;
         for (int i = 0; i < moveGen.Count; i++)
         {
            if (moveGen[i] == move)
            {
               found = true;
            }
         }

         Debug.Assert(found == ((moves.Value & SquareBB[move.To]) != 0));
#endif

         return (moves.Value & SquareBB[move.To]) != 0;
      }

      /// <summary>
      /// Static Exchange Evaluation Greater or Equal. Is <paramref name="move"/> better than <paramref name="threshold"/>?
      /// </summary>
      public bool SEE_GE(Move move, int threshold)
      {
         if (move.IsCastle() || move.HasType(MoveType.Promotion) || move.Flag == MoveFlag.EPCapture)
         {
            return threshold <= 0;
         }

         int from = move.From;
         int to = move.To;

         int swap = SEE_VALUES[(int)Squares[to].Type] - threshold;
         if (swap < 0)
         {
            return false;
         }

         swap = SEE_VALUES[(int)Squares[from].Type] - swap;
         if (swap <= 0)
         {
            return true;
         }

         ulong occupied = ((ColorBB[(int)Color.White] | ColorBB[(int)Color.Black]).Value ^ SquareBB[from]) | SquareBB[to];

         ulong attackers = AttackersTo(to, occupied);
         int stm = (int)SideToMove;
         int res = 1;
         ulong stmAttackers, bb;

         while (true)
         {
            stm ^= 1;
            attackers &= occupied;

            stmAttackers = attackers & ColorBB[stm].Value;
            if (stmAttackers == 0)
            {
               break;
            }

            res ^= 1;

            if ((bb = stmAttackers & PieceBB[(int)PieceType.Pawn].Value) != 0)
            {
               occupied ^= SquareBB[Bitboard.LSB(bb)];

               if ((swap = SEE_VALUES[(int)PieceType.Pawn] - swap) < res)
               {
                  break;
               }

               attackers |= GetBishopAttacks(to, occupied) & (PieceBB[(int)PieceType.Bishop] | PieceBB[(int)PieceType.Queen]).Value;
            }
            else if ((bb = stmAttackers & PieceBB[(int)PieceType.Knight].Value) != 0)
            {
               occupied ^= SquareBB[Bitboard.LSB(bb)];

               if ((swap = SEE_VALUES[(int)PieceType.Knight] - swap) < res)
               {
                  break;
               }
            }
            else if ((bb = stmAttackers & PieceBB[(int)PieceType.Bishop].Value) != 0)
            {
               occupied ^= SquareBB[Bitboard.LSB(bb)];

               if ((swap = SEE_VALUES[(int)PieceType.Bishop] - swap) < res)
               {
                  break;
               }

               attackers |= GetBishopAttacks(to, occupied) & (PieceBB[(int)PieceType.Bishop] | PieceBB[(int)PieceType.Queen]).Value;
            }
            else if ((bb = stmAttackers & PieceBB[(int)PieceType.Rook].Value) != 0)
            {
               occupied ^= SquareBB[Bitboard.LSB(bb)];

               if ((swap = SEE_VALUES[(int)PieceType.Rook] - swap) < res)
               {
                  break;
               }

               attackers |= GetRookAttacks(to, occupied) & (PieceBB[(int)PieceType.Rook] | PieceBB[(int)PieceType.Queen]).Value;
            }
            else if ((bb = stmAttackers & PieceBB[(int)PieceType.Queen].Value) != 0)
            {
               occupied ^= SquareBB[Bitboard.LSB(bb)];

               if ((swap = SEE_VALUES[(int)PieceType.Queen] - swap) < res)
               {
                  break;
               }

               attackers |= (GetBishopAttacks(to, occupied) & (PieceBB[(int)PieceType.Bishop] | PieceBB[(int)PieceType.Queen]).Value)
                  | (GetRookAttacks(to, occupied) & (PieceBB[(int)PieceType.Rook] | PieceBB[(int)PieceType.Queen]).Value);
            }
            else
            {
               return ((attackers & ~ColorBB[stm].Value) != 0) ? (res ^ 1) != 0 : res != 0;
            }
         }

         return res != 0;
      }

      private int VerifyPhase()
      {
         int phase = 0;
         Bitboard pieces = new(ColorBB[(int)Color.White].Value | ColorBB[(int)Color.Black].Value);

         while (pieces)
         {
            int square = pieces.GetLSB();
            pieces.ClearLSB();
            Piece piece = Squares[square];

            phase += PHASE_VALUES[(int)piece.Type];
         }

         return phase;
      }
   }
}
