// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using ILLink.Shared.DataFlow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
	public readonly struct TrimAnalysisPatternStore
	{
		readonly Dictionary<(IOperation, bool), TrimAnalysisAssignmentPattern> AssignmentPatterns;
		readonly Dictionary<IOperation, TrimAnalysisFieldAccessPattern> FieldAccessPatterns;
		readonly Dictionary<IOperation, TrimAnalysisMethodCallPattern> MethodCallPatterns;
		readonly Dictionary<IOperation, TrimAnalysisReflectionAccessPattern> ReflectionAccessPatterns;
		readonly ValueSetLattice<SingleValue> Lattice;

		public TrimAnalysisPatternStore (ValueSetLattice<SingleValue> lattice)
		{
			AssignmentPatterns = new Dictionary<(IOperation, bool), TrimAnalysisAssignmentPattern> ();
			FieldAccessPatterns = new Dictionary<IOperation, TrimAnalysisFieldAccessPattern> ();
			MethodCallPatterns = new Dictionary<IOperation, TrimAnalysisMethodCallPattern> ();
			ReflectionAccessPatterns = new Dictionary<IOperation, TrimAnalysisReflectionAccessPattern> ();
			Lattice = lattice;
		}

		public void Add (TrimAnalysisAssignmentPattern trimAnalysisPattern, bool isReturnValue)
		{
			// Finally blocks will be analyzed multiple times, once for normal control flow and once
			// for exceptional control flow, and these separate analyses could produce different
			// trim analysis patterns.
			// The current algorithm always does the exceptional analysis last, so the final state for
			// an operation will include all analysis patterns (since the exceptional state is a superset)
			// of the normal control-flow state.
			// We still add patterns to the operation, rather than replacing, to make this resilient to
			// changes in the analysis algorithm.
			if (!AssignmentPatterns.TryGetValue ((trimAnalysisPattern.Operation, isReturnValue), out var existingPattern)) {
				AssignmentPatterns.Add ((trimAnalysisPattern.Operation, isReturnValue), trimAnalysisPattern);
				return;
			}

			AssignmentPatterns[(trimAnalysisPattern.Operation, isReturnValue)] = trimAnalysisPattern.Merge (Lattice, existingPattern);
		}

		public void Add (TrimAnalysisFieldAccessPattern pattern)
		{
			if (!FieldAccessPatterns.TryGetValue (pattern.Operation, out var existingPattern)) {
				FieldAccessPatterns.Add (pattern.Operation, pattern);
				return;
			}

			// No Merge - there's nothing to merge since this pattern is uniquely identified by both the origin and the entity
			// and there's only one way to "access" a field.
			Debug.Assert (existingPattern == pattern, "Field access patterns should be identical");
		}

		public void Add (TrimAnalysisMethodCallPattern pattern)
		{
			if (!MethodCallPatterns.TryGetValue (pattern.Operation, out var existingPattern)) {
				MethodCallPatterns.Add (pattern.Operation, pattern);
				return;
			}

			MethodCallPatterns[pattern.Operation] = pattern.Merge (Lattice, existingPattern);
		}

		public void Add (TrimAnalysisReflectionAccessPattern pattern)
		{
			if (!ReflectionAccessPatterns.TryGetValue (pattern.Operation, out var existingPattern)) {
				ReflectionAccessPatterns.Add (pattern.Operation, pattern);
				return;
			}

			// No Merge - there's nothing to merge since this pattern is uniquely identified by both the origin and the entity
			// and there's only one way to access the referenced method.
			Debug.Assert (existingPattern == pattern, "Reflection access patterns should be identical");
		}

		public IEnumerable<Diagnostic> CollectDiagnostics (DataFlowAnalyzerContext context)
		{
			foreach (var assignmentPattern in AssignmentPatterns.Values) {
				foreach (var diagnostic in assignmentPattern.CollectDiagnostics (context))
					yield return diagnostic;
			}

			foreach (var fieldAccessPattern in FieldAccessPatterns.Values) {
				foreach (var diagnostic in fieldAccessPattern.CollectDiagnostics (context))
					yield return diagnostic;
			}

			foreach (var methodCallPattern in MethodCallPatterns.Values) {
				foreach (var diagnostic in methodCallPattern.CollectDiagnostics (context))
					yield return diagnostic;
			}

			foreach (var reflectionAccessPattern in ReflectionAccessPatterns.Values) {
				foreach (var diagnostic in reflectionAccessPattern.CollectDiagnostics (context))
					yield return diagnostic;
			}
		}
	}
}
