namespace OpenTPW;

partial class OpcodeHandlers
{
	/// <summary>
	/// Engine "object" opcodes — they don't change VM state, they ask the ride engine to do something
	/// in the world (spawn objects, play sounds, animate, …). Each routes to <see cref="RideVM.Engine"/>;
	/// with no engine attached (unit tests / headless) the null-conditional call is a no-op, identical
	/// to the previous "no handler" behaviour. Operand counts match docs/06-rse-vm-opcodes.md. The
	/// WAIT* opcodes suspend the script with the same PC-rewind trick as WAIT (Handlers/Time.cs). The
	/// `_CH` variants target the active child's objects; the child shares this VM's engine, so they
	/// route to the same registry for now. See docs/tickets/T-032 / T-007.
	/// </summary>
	public static class Objects
	{
		// Operand roles reversed from the executor (op_8 → FUN_00557970): the record takes the type (op 1) and the
		// node (op 2); FUN_005573d0( type, node, code = op 3, volume 1000 ) makes the effect; op 4 is the handle the
		// script later addresses (bus.RSE: ADDOBJ 8,1,6,1 … SETOBJPARAM 1,20,v).
		[OpcodeHandler( Opcode.ADDOBJ, "Spawn a ride effect object (sound / particle) under a handle." )]
		public static void AddObj( ref RideVM vm, Operand type, Operand node, Operand code, Operand id )
			=> vm.Engine?.SpawnObject( type.Value, node.Value, code.Value, id.Value );

		[OpcodeHandler( Opcode.ADDOBJ_EXT, "Spawn a ride object — the 5-operand extended ADDOBJ." )]
		public static void AddObjExt( ref RideVM vm, Operand type, Operand node, Operand code, Operand volume, Operand extra )
		{
			// RE'd (op_9 == op_8 + one extra operand → record[6]; both go through FUN_00557970 →
			// FUN_005573d0). The real operand roles are (type, node, code, volume): FUN_005573d0's type
			// switch routes type 1-2 to the positioned-sound funcs (the same FUN_00521e60/930 EVENT uses)
			// and type 3-10 to the particle spawner (FUN_0051bfc0). We register the object like ADDOBJ —
			// same 4-arg SpawnObject call, positionally (type, parameter, id, slot) — for KILLOBJ, then
			// render the particle case via the decoded .PLB (T-019). Type 1-2 sound is deliberately deferred
			// for ADDOBJ (T-037 wrong-clip risk), so we don't play it here either; the 5th operand
			// (record[6]) has no decoded runtime effect. No shipped .rse uses ADDOBJ_EXT (0 of 123 scripts).
			// (The extended form carries the volume in op 4 and the handle in op 5.)
			vm.Engine?.SpawnObject( type.Value, node.Value, code.Value, extra.Value );
		}

		[OpcodeHandler( Opcode.SPAWNSOUND, "Play (or set up) a sound named by a string operand." )]
		public static void SpawnSound( ref RideVM vm, Operand sound )
		{
			// The operand is a string (sound name / sound-event-map script) — RE'd: the handler requires
			// the string type tag (FUN_005551ab). Resolve it; a non-string operand is a no-op (as in the
			// original). Passing the raw offset to the engine was the old "wrong clip" bug (T-037).
			if ( vm.Strings.TryGetValue( sound.Value, out var name ) )
				vm.Engine?.PlaySound( name );
		}

		[OpcodeHandler( Opcode.KILLOBJ, "Remove a spawned object by its handle." )]
		public static void KillObj( ref RideVM vm, Operand id )
			=> vm.Engine?.KillObject( id.Value );

		[OpcodeHandler( Opcode.FADEOBJ, "Fade out and remove an object (stage 1: remove)." )]
		public static void FadeObj( ref RideVM vm, Operand id )
			=> vm.Engine?.KillObject( id.Value );

		[OpcodeHandler( Opcode.SETOBJPARAM, "Set an object parameter." )]
		public static void SetObjParam( ref RideVM vm, Operand id, Operand param, Operand value )
			=> vm.Engine?.SetObjectParam( id.Value, param.Value, value.Value );

		[OpcodeHandler( Opcode.TRIGANIM, "Trigger an animation on an object (once)." )]
		public static void TrigAnim( ref RideVM vm, Operand id, Operand anim, Operand _ )
			=> vm.Engine?.TriggerAnim( id.Value, anim.Value, loop: false );

		[OpcodeHandler( Opcode.LOOPANIM, "Loop an animation on an object." )]
		public static void LoopAnim( ref RideVM vm, Operand id, Operand anim )
			=> vm.Engine?.TriggerAnim( id.Value, anim.Value, loop: true );

		[OpcodeHandler( Opcode.TRIGANIMSPEED, "Trigger an animation at a given speed." )]
		public static void TrigAnimSpeed( ref RideVM vm, Operand id, Operand anim, Operand speed, Operand _ )
		{
			vm.Engine?.TriggerAnim( id.Value, anim.Value, loop: false );
			vm.Engine?.SetAnimSpeed( id.Value, speed.Value );
		}

		[OpcodeHandler( Opcode.WAITANIM, "Suspend the script until the object's animation finishes." )]
		public static void WaitAnim( ref RideVM vm, Operand id, Operand anim )
		{
			if ( vm.Engine?.IsAnimating( id.Value, anim.Value ) == true )
				vm.CurrentPos--; // still animating: re-run this instruction next tick
		}

		[OpcodeHandler( Opcode.TRIGWAITANIM, "Trigger an animation and suspend until it finishes." )]
		public static void TrigWaitAnim( ref RideVM vm, Operand id, Operand anim, Operand _ )
		{
			vm.Engine?.TriggerAnim( id.Value, anim.Value, loop: false ); // idempotent re-trigger
			if ( vm.Engine?.IsAnimating( id.Value, anim.Value ) == true )
				vm.CurrentPos--;
		}

		[OpcodeHandler( Opcode.WAIT4ANIM, "Suspend the script until all animations finish." )]
		public static void Wait4Anim( ref RideVM vm )
		{
			if ( vm.Engine?.AnyAnimating() == true )
				vm.CurrentPos--;
		}

		[OpcodeHandler( Opcode.FLUSHANIM, "Stop all animations." )]
		public static void FlushAnim( ref RideVM vm )
			=> vm.Engine?.FlushAnims( -1 );

		[OpcodeHandler( Opcode.GETANIM, "Read the current animation of an object into dest." )]
		public static void GetAnim( ref RideVM vm, Operand dest )
		{
			if ( vm.Engine != null )
				dest.Value = vm.Engine.GetAnim( dest.Value );
		}

		//
		// Rider scream (e.g. monkey, totem): operand 1 = scream sound code, operand 2 = volume level
		// (0..100, -1 = default). See docs/06-rse-vm-opcodes.md / T-032.
		//

		[OpcodeHandler( Opcode.STARTSCREAM, "Begin a sustained rider scream (code, level)." )]
		public static void StartScream( ref RideVM vm, Operand code, Operand level )
			=> vm.Engine?.StartScream( code.Value, level.Value );

		[OpcodeHandler( Opcode.STOPSCREAM, "End the sustained rider scream." )]
		public static void StopScream( ref RideVM vm )
			=> vm.Engine?.StopScream();

		[OpcodeHandler( Opcode.SINGLESCREAM, "Play a one-shot rider scream (code, level)." )]
		public static void SingleScream( ref RideVM vm, Operand code, Operand level )
			=> vm.Engine?.SingleScream( code.Value, level.Value );

		[OpcodeHandler( Opcode.SCREAMLEVEL, "Set the rider scream volume level." )]
		public static void ScreamLevel( ref RideVM vm, Operand level )
			=> vm.Engine?.SetScreamLevel( level.Value );

		//
		// _CH variants — same actions, targeting the active child's objects (shared engine for now).
		// They carry one extra leading operand (the child handle) that we don't distinguish yet.
		//

		[OpcodeHandler( Opcode.TRIGANIM_CH, "Trigger an animation on a child's object." )]
		public static void TrigAnimCh( ref RideVM vm, Operand _, Operand id, Operand anim, Operand __ )
			=> vm.ActiveChild?.Engine?.TriggerAnim( id.Value, anim.Value, loop: false );

		[OpcodeHandler( Opcode.LOOPANIM_CH, "Loop an animation on a child's object." )]
		public static void LoopAnimCh( ref RideVM vm, Operand _, Operand id, Operand anim )
			=> vm.ActiveChild?.Engine?.TriggerAnim( id.Value, anim.Value, loop: true );

		[OpcodeHandler( Opcode.WAITANIM_CH, "Suspend until a child's object animation finishes." )]
		public static void WaitAnimCh( ref RideVM vm, Operand _, Operand id, Operand anim )
		{
			if ( vm.ActiveChild?.Engine?.IsAnimating( id.Value, anim.Value ) == true )
				vm.CurrentPos--;
		}

		[OpcodeHandler( Opcode.TRIGWAITANIM_CH, "Trigger + suspend on a child's object animation." )]
		public static void TrigWaitAnimCh( ref RideVM vm, Operand _, Operand id, Operand anim, Operand __ )
		{
			vm.ActiveChild?.Engine?.TriggerAnim( id.Value, anim.Value, loop: false );
			if ( vm.ActiveChild?.Engine?.IsAnimating( id.Value, anim.Value ) == true )
				vm.CurrentPos--;
		}

		[OpcodeHandler( Opcode.FLUSHANIM_CH, "Stop a child's animations." )]
		public static void FlushAnimCh( ref RideVM vm, Operand _ )
			=> vm.ActiveChild?.Engine?.FlushAnims( -1 );

		[OpcodeHandler( Opcode.GETANIM_CH, "Read a child's object current animation into dest." )]
		public static void GetAnimCh( ref RideVM vm, Operand _, Operand dest )
		{
			if ( vm.ActiveChild?.Engine != null )
				dest.Value = vm.ActiveChild.Engine.GetAnim( dest.Value );
		}
	}
}
