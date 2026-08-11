#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;

/// <summary>
/// EDITOR ONLY. Must live in a folder named "Editor".
///
/// Dumps every parameter, state, and transition (with conditions) of an
/// AnimatorController to the Console so the graph can be inspected as text.
///
/// Usage: select the .controller asset in the Project window,
///        then Tools > Dump Animator Controller.
/// </summary>
public static class AnimatorGraphDumper
{
    [MenuItem("Tools/Dump Animator Controller")]
    private static void Dump()
    {
        AnimatorController ac = Selection.activeObject as AnimatorController;

        if (ac == null)
        {
            // Also accept selecting a GameObject with an Animator.
            GameObject go = Selection.activeGameObject;
            if (go != null)
            {
                Animator a = go.GetComponent<Animator>() ?? go.GetComponentInChildren<Animator>();
                if (a != null) ac = a.runtimeAnimatorController as AnimatorController;
            }
        }

        if (ac == null)
        {
            Debug.LogError("Select an AnimatorController asset (.controller) in the Project window, " +
                           "or a GameObject with an Animator, then run Tools > Dump Animator Controller.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════");
        sb.AppendLine($"CONTROLLER: {ac.name}");
        sb.AppendLine($"Path: {AssetDatabase.GetAssetPath(ac)}");
        sb.AppendLine("═══════════════════════════════════════════════════");

        sb.AppendLine($"\nPARAMETERS ({ac.parameters.Length}):");
        foreach (var p in ac.parameters)
            sb.AppendLine($"    {p.name,-24} {p.type}");

        foreach (var layer in ac.layers)
        {
            sb.AppendLine($"\n─── LAYER: {layer.name} ───");
            DumpMachine(layer.stateMachine, sb, "");
        }

        Debug.Log(sb.ToString());

        string file = "AnimatorDump_" + ac.name + ".txt";
        System.IO.File.WriteAllText(file, sb.ToString());
        Debug.Log($"Also written to: {System.IO.Path.GetFullPath(file)}");
    }

    private static void DumpMachine(AnimatorStateMachine sm, StringBuilder sb, string indent)
    {
        sb.AppendLine($"{indent}DEFAULT STATE: {(sm.defaultState != null ? sm.defaultState.name : "<none>")}");

        sb.AppendLine($"{indent}\nANY STATE TRANSITIONS ({sm.anyStateTransitions.Length}) — evaluated top to bottom:");
        for (int i = 0; i < sm.anyStateTransitions.Length; i++)
            DumpTransition(sm.anyStateTransitions[i], sb, indent + "    ", $"[{i}] AnyState");

        sb.AppendLine($"{indent}\nSTATES ({sm.states.Length}):");
        foreach (var cs in sm.states)
        {
            var st = cs.state;
            string clip = st.motion != null ? st.motion.name : "<NO CLIP>";
            sb.AppendLine($"{indent}  ▸ {st.name}   clip={clip}   speed={st.speed}");

            if (st.transitions.Length == 0)
                sb.AppendLine($"{indent}      (no outgoing transitions)");

            foreach (var t in st.transitions)
                DumpTransition(t, sb, indent + "      ", st.name);
        }

        foreach (var child in sm.stateMachines)
        {
            sb.AppendLine($"{indent}\n  ── SUB-MACHINE: {child.stateMachine.name} ──");
            DumpMachine(child.stateMachine, sb, indent + "    ");
        }
    }

    private static void DumpTransition(AnimatorStateTransition t, StringBuilder sb, string indent, string from)
    {
        string dest = t.destinationState != null ? t.destinationState.name
                    : t.destinationStateMachine != null ? t.destinationStateMachine.name + " (machine)"
                    : t.isExit ? "EXIT" : "<none>";

        sb.AppendLine($"{indent}{from} -> {dest}");
        sb.AppendLine($"{indent}    hasExitTime={t.hasExitTime}  exitTime={t.exitTime}  " +
                      $"duration={t.duration}  canTransitionToSelf={t.canTransitionToSelf}  " +
                      $"interruptionSource={t.interruptionSource}");

        if (t.conditions.Length == 0)
            sb.AppendLine($"{indent}    conditions: NONE");
        else
            foreach (var c in t.conditions)
                sb.AppendLine($"{indent}    condition: {c.parameter} {c.mode} {c.threshold}");
    }
}
#endif