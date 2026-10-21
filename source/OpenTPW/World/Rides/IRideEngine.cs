namespace OpenTPW;

/// <summary>
/// The runtime that a ride's RSE script drives. The VM's "engine" opcodes (Batch B) call into this;
/// when a VM has no engine attached (unit tests, headless tools) the calls are guarded no-ops, so
/// the pure-VM behaviour is unchanged. Stage 1 covers object spawning, sound, object lifecycle and
/// (procedural) animation; later stages add real keyframe animation, lights, peeps and coasters (see
/// docs/tickets/T-007 + T-032).
/// </summary>
public interface IRideEngine
{
	/// <summary>ADDOBJ — register an object (sound / particle / …) under the script's handle + slot.</summary>
	/// <summary>ADDOBJ (reversed, op_8 → FUN_00557970 → FUN_005573d0): an effect object of <paramref name="type"/>
	/// (1–2 a positioned sound, 3–10 a particle effect) at model node <paramref name="node"/> (−1 the ride), playing
	/// <paramref name="code"/>, addressed afterwards (KILLOBJ / SETOBJPARAM) by <paramref name="id"/>.</summary>
	void SpawnObject( int type, int node, int code, int id );

	/// <summary>SPAWNSOUND — play (or set up) a sound named by the script. The operand is a string: a
	/// sound file name, or a sound-event-map script (e.g. <c>EventMap.rse</c>).</summary>
	void PlaySound( string name );

	/// <summary>KILLOBJ — remove a previously-spawned object by its handle id (and despawn its model).</summary>
	void KillObject( int id );

	/// <summary>SETOBJPARAM — set parameter <paramref name="param"/> of object <paramref name="id"/>.</summary>
	void SetObjectParam( int id, int param, int value );

	/// <summary>TRIGANIM / LOOPANIM — start animation <paramref name="anim"/> on object <paramref name="id"/>.</summary>
	void TriggerAnim( int id, int anim, bool loop );

	/// <summary>TRIGANIMSPEED — set the animation playback speed of object <paramref name="id"/>.</summary>
	void SetAnimSpeed( int id, float speed );

	/// <summary>FLUSHANIM — stop the animation on object <paramref name="id"/> (id &lt; 0 = all).</summary>
	void FlushAnims( int id );

	/// <summary>GETANIM — the current animation of object <paramref name="id"/> (0 if none).</summary>
	int GetAnim( int id );

	/// <summary>WAITANIM — whether object <paramref name="id"/> is still playing the given (non-looping) anim.</summary>
	bool IsAnimating( int id, int anim );

	/// <summary>WAIT4ANIM — whether any object is still playing a (non-looping) animation.</summary>
	bool AnyAnimating();

	/// <summary>STARTSCREAM — begin a sustained rider scream (sound <paramref name="code"/>) at volume
	/// <paramref name="level"/> (0..100; &lt;0 = keep current).</summary>
	void StartScream( int code, int level );

	/// <summary>STOPSCREAM — end the sustained scream.</summary>
	void StopScream();

	/// <summary>SINGLESCREAM — play a one-shot scream (sound <paramref name="code"/>) at <paramref name="level"/>.</summary>
	void SingleScream( int code, int level );

	/// <summary>SCREAMLEVEL — set the scream volume level (0..100; &lt;0 = unchanged).</summary>
	void SetScreamLevel( int level );

	/// <summary>COAST — multiplexed coaster control (subcommand + arg). Subcommand map RE'd from
	/// coaster1.rse: 1 load rider · 2 can-load? · 3 peep-wants-off? · 4 state · 5 mode · 6 capacity ·
	/// 7 worn · 8 init. Query subcommands' Zero-flag result is set by the opcode handler.</summary>
	void Coast( int sub, int arg );

	/// <summary>TOUR — multiplexed tour-ride control (subcommand + arg). RE'd from op_53: 1 initialise ·
	/// 2 shut down · 3/4/10/11/15/16 queries · the rest setters, all onto a tour-ride object class
	/// (FUN_0055a620 et al). Query subcommands' result is set by the opcode handler.</summary>
	void Tour( int sub, int arg );

	/// <summary>BUMP — multiplexed bumper/kart-ride control (subcommand + arg). RE'd from op_54: 1 add peep
	/// · 3 start · 4 add car · 7 open · 11 cars-on-ride · 13 set laps · 16 remove car · 17 set open;
	/// queries {1,2,4,5,11,12,16} write the result register read by the following BRANCH.</summary>
	void Bump( int sub, int arg );

	/// <summary>EVENT / EVENT_EXT — fire a ride event (type + params): sounds, effects, messages, …</summary>
	void Event( int type, int p1, int p2 );

	/// <summary>ENABLELIGHT — turn on the ride light addressed by <paramref name="id"/>.</summary>
	void EnableLight( int id );

	/// <summary>DISABLELIGHT — turn off the ride light <paramref name="id"/>.</summary>
	void DisableLight( int id );

	/// <summary>SETLIGHT — set light <paramref name="id"/>'s brightness (0..1; the script's 0..100 ÷ 100).</summary>
	void SetLight( int id, float brightness );

	/// <summary>COLOURLIGHT — set light <paramref name="id"/>'s colour (each channel 0..1; the script's 0..100 ÷ 100).</summary>
	void ColourLight( int id, float r, float g, float b );

	/// <summary>Spawn a particle effect by its <c>par_lib.h</c> <c>P_EFFECT_*</c> code at the ride (the
	/// <c>.PLB</c>-driven effect system behind REPAIREFFECT / SPARK).</summary>
	void SpawnParticleEffect( int effectCode );

	/// <summary>Spawn a particle effect at the world position of ride node <paramref name="nodeId"/>
	/// (REPAIREFFECT/SPARK resolve a node before spawning — <c>FUN_00556b90</c>; see T-048/T-047). Falls
	/// back to the ride centre when the node can't be placed.</summary>
	void SpawnParticleEffect( int effectCode, int nodeId );

	/// <summary>Emit the breakdown feedback (sparks + an electrical zap) at the ride — played periodically by
	/// a broken-down ride until it's repaired (T-039).</summary>
	void PlayBreakdownEffect();

	/// <summary>SETREVERB — set the audio reverb preset/level.</summary>
	void SetReverb( int level );

	/// <summary>DIPMUSIC — briefly duck the background music.</summary>
	void DipMusic( int amount );
}
