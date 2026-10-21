namespace OpenTPW;

public partial class OpcodeHandlers
{
	public static class Misc
	{
		[OpcodeHandler( Opcode.NOP, "No-op" )]
		public static void NoOp( ref RideVM vm )
		{
			// Do nothing
		}

		// CRIT_LOCK/CRIT_UNLOCK guard the script's critical section (e.g. the coaster load loop). With a
		// single-threaded VM there's no contention, so they're no-ops. Not logged — ride loops fire them
		// thousands of times per second.
		[OpcodeHandler( Opcode.CRIT_LOCK, "Crit lock (no-op: single-threaded VM)." )]
		public static void CritLock( ref RideVM vm ) { }

		[OpcodeHandler( Opcode.CRIT_UNLOCK, "Crit unlock (no-op: single-threaded VM)." )]
		public static void CritUnlock( ref RideVM vm ) { }

		[OpcodeHandler( Opcode.COPY, "Copy a value" )]
		public static void Copy( ref RideVM vm, Operand dest, Operand source )
		{
			dest.Value = source.Value;
		}

		[OpcodeHandler( Opcode.NAME, "Set ride name" )]
		public static void Name( ref RideVM vm, Operand newName )
		{
			vm.ScriptName = vm.Strings[newName.Value];
			Log.Trace( $"Set ride name to {vm.ScriptName}" );
		}

		[OpcodeHandler( Opcode.SETLV, "Set level" )]
		public static void SetLv( ref RideVM vm, Operand unknown )
		{
			Log.Trace( "TODO: Set level" );
		}

		[OpcodeHandler( Opcode.ENDSLICE, "End current slice" )]
		public static void EndSlice( ref RideVM vm )
		{
			vm.SliceEnded = true; // yield the rest of this tick's slice (see RideVM.RunSlice)
		}

		// ADDOBJ now lives in Handlers/Objects.cs (routes to the ride engine). It must exist in
		// exactly one place — duplicate [OpcodeHandler] attributes make the reflection registration's
		// ToDictionary throw on the duplicate opcode key.

		[OpcodeHandler( Opcode.DBGMSG, "Debug Message" )]
		public static void DbgMsg( ref RideVM vm, Operand value )
		{
			// I don't even think this is used anywhere, but it has been implemented for completeness' sake.
			Log.Trace( $"Debug message: {vm.Strings[value.Value]}" );
		}

		[OpcodeHandler( Opcode.RAND, "Generate a random number" )]
		public static void Random( ref RideVM vm, Operand dest, Operand maxValue )
		{
			var random = System.Random.Shared.Next( 0, maxValue.Value );
			dest.Value = random;
		}

		[OpcodeHandler( Opcode.PUSH, "Push a value onto the stack." )]
		public static void Push( ref RideVM vm, Operand value )
		{
			vm.Stack.Push( value.Value );
		}

		[OpcodeHandler( Opcode.POP, "Pop the top stack value into the destination." )]
		public static void Pop( ref RideVM vm, Operand dest )
		{
			// POP takes one operand (confirmed from the binary's opcode table): it writes the
			// popped value into the destination. Guard against an underflow.
			if ( vm.Stack.Count == 0 )
			{
				Log.Warning( "POP on an empty stack; ignoring" );
				return;
			}

			dest.Value = vm.Stack.Pop();
		}

		[OpcodeHandler( Opcode.HUSH, "Push a value onto the secondary (HUSH/HOP) stack." )]
		public static void Hush( ref RideVM vm, Operand value )
		{
			vm.HushStack.Push( value.Value );
		}

		[OpcodeHandler( Opcode.HOP, "Pop the secondary stack into the destination." )]
		public static void Hop( ref RideVM vm, Operand dest )
		{
			// HUSH/HOP are a second LIFO (the bottom end of the original's double-ended stack),
			// distinct from PUSH/POP. Guard against an underflow.
			if ( vm.HushStack.Count == 0 )
			{
				Log.Warning( "HOP on an empty stack; ignoring" );
				return;
			}

			dest.Value = vm.HushStack.Pop();
		}
	}
}
