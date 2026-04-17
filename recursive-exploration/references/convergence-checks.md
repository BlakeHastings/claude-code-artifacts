# Convergence Checks & Quality Gates

Comprehensive stopping criteria and quality gates for determining when recursive exploration has converged to a reliable final answer.

---

## When to Stop Iterating

### 1. Convergence Detection: No Meaningful Improvement

The primary stopping criterion is detecting when further refinement yields diminishing returns.

#### Quantitative Thresholds

```python
def check_convergence(current_output, previous_output):
    """
    Detect convergence by measuring improvement between iterations.
    
    Returns True if exploration should stop (no significant improvement).
    """
    
    # Extract key metrics from both outputs
    current_metrics = extract_quality_metrics(current_output)
    prev_metrics = extract_quality_metrics(previous_output)
    
    # Calculate improvement score across dimensions
    improvements = {
        'completeness': calculate_improvement(
            current_metrics['completeness'], 
            prev_metrics['completeness']
        ),
        'accuracy_score': calculate_improvement(
            current_metrics['accuracy_score'],
            prev_metrics['accuracy_score']
        ),
        'clarity_score': calculate_improvement(
            current_metrics['clarity_score'],
            prev_metrics['clarity_score']
        )
    }
    
    # Average improvement across all dimensions
    avg_improvement = sum(improvements.values()) / len(improvements)
    
    # CONVERGENCE THRESHOLD: <15% improvement
    if avg_improvement < 0.15:
        return True, f"Converged: only {avg_improvement:.2f} improvement from last iteration"
    
    return False, None

# Usage in main loop
for iteration in range(MAX_ITERATIONS):
    improved = check_convergence(current_response, previous_response)
    if improved[0]:  # Converged!
        break
```

#### Qualitative Convergence Signals

Beyond numbers, watch for these qualitative signs:

| Signal | Description | Stop Recommendation |
|--------|-------------|---------------------|
| **Repeated patterns** | Same arguments reappear in critique → refinement cycle | ✅ Strong signal to stop |
| **Diminishing new content** | Each iteration adds <10% new information | ✅ Continue 1-2 more checks then stop |
| **Critique saturation** | Critique focuses on same minor issues repeatedly | ✅ Stop and finalize |
| **Output stability** | Final answer text is >85% similar to previous iteration | ✅ Converged |

---

## Quality Gates Before Final Output

Before presenting final results, verify these quality gates pass:

### Gate 1: Multiple Sources Confirm Findings

```python
def verify_source_diversity(answer, context_sources):
    """Ensure answer is supported by multiple independent sources"""
    
    # Extract key claims from answer
    claims = extract_claims(answer)
    
    # Verify each claim has source backing
    verified_claims = 0
    for claim in claims:
        supporting_sources = find_supporting_sources(claim, context_sources)
        
        if len(supporting_sources) >= 2:  # At least 2 sources confirm
            verified_claims += 1
    
    pass_threshold = (verified_claims / len(claims)) > 0.6  # 60%+ claims backed
    
    return {
        'pass': pass_threshold,
        'supported_ratio': verified_claims / max(len(claims), 1),
        'details': [
            {'claim': c, 'sources': s} 
            for c, s in zip(claims, [find_supporting_sources(c, context_sources) for c in claims])
        ]
    }

# Quality gate check
source_diversity = verify_source_divery(final_answer, all_context_used)
if not source_diversity['pass']:
    warn("Final answer lacks multi-source verification")
```

### Gate 2: Alternative Viewpoints Considered

```python
def check_alternatives_considered(answer):
    """Ensure opposing or alternative perspectives are addressed"""
    
    # Generate counterarguments (LLM-based)
    prompt = f"""Given this analysis: {answer}

Generate 3-5 potential counterarguments, limitations, or 
alternative interpretations that could challenge these conclusions.

Format as list of tuples: (counterargument, strength_estimate, validity_evidence)"""
    
    counterarguments = generate(prompt)
    
    # Check if any were addressed in original answer
    original_conclusions = extract_conclusions(answer)
    
    addressed_count = 0
    for counter in counterarguments[:5]:  # Check top 5 strongest counterarguments
        if any(c.lower() in str(counter).lower() or 
               c.lower() in counter['text'].lower() 
               for c in original_conclusions):
            addressed_count += 1
    
    pass_threshold = addressed_count >= 2  # At least 2 counters addressed
    
    return {
        'pass': pass_threshold,
        'counterarguments_found': len(counterarguments),
        'addressed_count': addressed_count
    }

# Quality gate check  
alternatives_check = check_alternatives_considered(final_answer)
if not alternatives_check['pass']:
    warn("Not enough alternative viewpoints considered")
```

### Gate 3: Edge Cases and Exceptions Documented

```python
def verify_edge_cases_documented(answer, topic):
    """Check that limitations and edge cases are acknowledged"""
    
    # Generate potential edge cases for topic
    prompt = f"""For the topic: {topic}

List 5-10 important edge cases, exceptions, or boundary conditions 
that an expert would consider relevant but might not be obvious.

Format as JSON list of objects with: case_name, description, impact_level"""
    
    edge_cases = generate(prompt)
    
    # Check if answer addresses these
    answer_text = str(answer).lower()
    addressed_count = 0
    
    for case in edge_cases[:5]:
        keywords = [case['name'].lower(), case['description'].lower()]
        if any(kw.lower() in answer_text for kw in keywords):
            addressed_count += 1
    
    pass_threshold = addressed_count >= 2  # At least 2 edge cases mentioned
    
    return {
        'pass': pass_threshold,
        'edge_cases_identified': len(edge_cases),
        'addressed_count': addressed_count
    }

# Quality gate check
edge_cases_check = verify_edge_cases_documented(final_answer, topic)
if not edge_cases_check['pass']:
    warn("Edge cases and exceptions not sufficiently documented")
```

### Gate 4: Reasoning Chain Explicit and Traceable

```python
def verify_reasoning_traceability(answer):
    """Ensure reasoning can be followed step-by-step"""
    
    # Check for explicit reasoning markers
    reasoning_sections = re.findall(r'\[\[REASONING\]\].*?(?=\n\n|\Z)', str(answer), re.DOTALL)
    
    if len(reasoning_sections) < 2:
        return {
            'pass': False,
            'reasoning_markers_found': len(reasoning_sections),
            'issue': "Missing explicit reasoning trace"
        }
    
    # Verify each conclusion is supported by preceding reasoning
    conclusions = re.findall(r'\nFinal.*?(?=\n\n|\Z)', str(answer), re.DOTALL)
    
    if len(conclusions) < 1:
        return {
            'pass': False, 
            'reasoning_markers_found': len(reasoning_sections),
            'issue': "No clear final conclusion identified"
        }
    
    # Check that conclusions reference reasoning sections
    all_referenced = True
    for i, conclusion in enumerate(conclusions):
        ref_count = sum(1 for r in reasoning_sections 
                       if any(c.lower() in r.lower() or i+1 == str(r).count('Phase'))
                       for c in extract_key_concepts(conclusion))
        
        # Each conclusion should reference at least one reasoning section
        all_referenced = all_referenced and ref_count >= 1
    
    return {
        'pass': all_referenced,
        'reasoning_markers_found': len(reasoning_sections),
        'conclusions_found': len(conclusions)
    }

# Quality gate check
traceability_check = verify_reasoning_traceability(final_answer)
if not traceability_check['pass']:
    warn("Reasoning chain is not explicit enough to be traced")
```

### Gate 5: Multiple Sources Confirm (Final Check)

```python
def final_quality_assessment(answer, topic, sources_used):
    """Comprehensive quality assessment before final output"""
    
    gates = {
        'source_diversity': verify_source_diversity(answer, sources_used),
        'alternatives_considered': check_alternatives_considered(answer),
        'edge_cases_documented': verify_edge_cases_documented(answer, topic),
        'reasoning_traceable': verify_reasoning_traceability(answer)
    }
    
    # All gates must pass (or have documented exceptions)
    all_passed = all(gate['pass'] for gate in gates.values())
    
    if not all_passed:
        failed_gates = [name for name, g in gates.items() if not g['pass']]
        
        # Generate exception notes
        exceptions = []
        for name, gate in gates.items():
            if not gate['pass']:
                reason = f"{gate.get('issue', 'Unknown')}"
                mitigation = generate_mitigation_reasoning(answer, name)
                exceptions.append(f"Gate '{name}': {reason}. Mitigation: {mitigation}")
        
        return {
            'final_decision': 'PARTIAL_PASS',
            'gates_passed': sum(1 for g in gates.values() if g['pass']),
            'gates_failed': len([g for g in gates.values() if not g['pass']]),
            'exceptions': exceptions,
            'recommendation': 'Proceed with warnings noted'
        }
    
    return {
        'final_decision': 'PASS',
        'gates_passed': 5,
        'gates_failed': 0,
        'exceptions': [],
        'recommendation': 'Ready for final output'
    }

# Final gate assessment before presenting answer
quality = final_quality_assessment(final_answer, topic, all_context_used)

if quality['final_decision'] == 'PASS':
    present_final_output()  # ✅ Ready!
elif quality['final_decision'] == 'PARTIAL_PASS':
    present_with_warnings(quality['exceptions'])  # ⚠️ Proceed with notes
else:
    escalate_for_review()  # ❌ Needs human review
```

---

## Escalation Criteria When Skill Can't Converge

After exhausting maximum retries, provide partial findings and specific guidance.

### Maximum Retry Logic (3 Strategy Switches)

```python
def handle_non_convergence(max_retries=3):
    """Called when exploration fails to converge after max attempts"""
    
    retry_count = 0
    
    while retry_count < max_retries:
        strategy = get_alternative_strategy(current_strategy)
        
        # Attempt with new approach
        result = explore_with_strategy(topic, strategy)
        
        if check_convergence(result['output'], last_output):
            return success(result['output'])
        
        last_output = result['output']
        retry_count += 1
    
    # All retries exhausted - escalate
    return escalate_for_review()
```

### Escalation Output Format

When unable to converge, provide structured partial findings:

```markdown
=== ESCALATED OUTPUT (Unable to Converge) ===

[[REASONING]] Final Assessment After Max Iterations

After {MAX_ITERATIONS} refinement cycles and 3 strategy switches, 
the exploration could not achieve reliable convergence. This is often 
indicative of:

1. Insufficient information in available sources
2. Ambiguous or underspecified question  
3. Complex multi-faceted topic requiring human judgment
4. Contradictory evidence across sources that cannot be resolved

=== PARTIAL FINDINGS ===

**What Was Discovered:**
- Primary concept identified: [TOPIC]
- Key related concepts found: [LIST]
- Main insights gathered: [SUMMARY]

**Confidence Level: LOW-MEDIUM (Estimated 45%)**

**Areas of Uncertainty:**
- [Unclear aspect 1]
- [Insufficient evidence for X]
- [Contradictory sources on Y]

=== SPECIFIC RECOMMENDATIONS FOR HUMAN FOLLOW-UP ===

1. **Additional Sources Needed**: 
   - Suggest searching: "[specific search term]"
   - Consider checking: "[related domain or expert source]"

2. **Clarification Questions** (if answer was vague):
   - Did you mean [alternative interpretation A]?
   - Or were you asking about [alternative interpretation B]?

3. **Manual Investigation Steps**:
   - Review these specific files: [FILE PATHS]
   - Cross-reference with external source: [URL or resource type]

[[REASONING]] Why This Needed Escalation

The convergence detection identified that further refinement would not 
improve accuracy meaningfully. The quality gates showed:

- Source diversity: {quality['source_diversity']['supported_ratio']:.0%} of claims backed
- Alternatives considered: {alternatives_check['addressed_count']}/{counterarguments_found} addressed  
- Edge cases documented: {edge_cases_check['addressed_count']}/5 identified
- Reasoning traceable: {traceability_check['reasoning_markers_found']} sections found

Proceeding with partial findings because continuing iteration would 
likely repeat same patterns without adding value.
```

---

## Quality Gate Checklist (Quick Reference)

Before finalizing any exploration output, verify these items:

| # | Gate Name | Check Method | Threshold | Pass? |
|---|-----------|--------------|-----------|-------|
| 1 | **Source Diversity** | Multiple independent sources per claim | ≥2 sources per 60%+ of claims | [ ] |
| 2 | **Alternatives Considered** | Counterarguments addressed in answer | ≥2 counterarguments addressed | [ ] |
| 3 | **Edge Cases Documented** | Limitations/exceptions acknowledged | ≥2 edge cases mentioned | [ ] |
| 4 | **Reasoning Traceable** | Explicit [[REASONING]] markers present | ≥2 reasoning sections, conclusions referenced | [ ] |
| 5 | **Improvement Plateau** | No >15% gain in last 2 iterations | Avg improvement <0.15 across dimensions | [ ] |

### Quick Decision Tree

```
┌─────────────────────────────────────┐
│  Did exploration converge?          │
│  (improvement <15% for 2 cycles?)   │
└──────────────┬──────────────────────┘
               │ YES → Continue to gates...
               │ NO  → More iteration needed
               
               ▼
        ┌─────────────────────────┐
        │  All quality gates pass? │
        │  (5 checks above)       │
        └──────────────┬───────────┘
                       │ YES → Final output ✅
                       │ NO   → Partial with warnings ⚠️
                            (Note failed gates)
                            
                            ▼
                    Escalate for review ❓
                    (After 3 retry attempts)
```

---

## Common Convergence Issues & Solutions

### Issue: Infinite Loops

**Symptoms**: Same critique-refine cycle repeats without improvement

**Detection**: 
- Output similarity >90% between consecutive iterations
- Critique text is nearly identical across cycles

**Solution**: Force strategy switch or escalate after 2 stuck cycles

```python
def detect_infinite_loop(current, previous):
    """Detect if we're spinning in circles"""
    
    # High similarity = potential infinite loop  
    if output_similarity_score(current, previous) > 0.9:
        return True
    
    # Same critique repeated?
    current_critique = extract_critique_section(current)
    prev_critique = extract_critique_section(previous)
    
    if critique_similarity(current_critique, prev_critique) > 0.85:
        return True
    
    return False

# Check before each iteration
if detect_infinite_loop(current_state, previous_state):
    force_strategy_switch()  # Try different approach
```

### Issue: Premature Convergence

**Symptoms**: Stopping too early because quality gates are too lenient

**Detection**: 
- Improvement still >15% but stopped anyway  
- Multiple sources not consulted yet

**Solution**: Relax convergence threshold or extend max iterations for complex topics

```python
def check_premature_convergence(improvement_score, iteration):
    """Check if we're stopping too early"""
    
    # Still improving significantly?
    if improvement_score > 0.25:  # >25% improvement
        return True
    
    # Early in process with little exploration done?
    if iteration < MAX_ITERATIONS * 0.4:
        return True
    
    return False

# Override convergence detection if premature
if detect_infinite_loop(current, previous):
    force_strategy_switch()
elif check_premature_convergence(improvement_score, iteration):
    log_warning("Stopping early - consider more iterations")
    continue_iteration = ask_user_confirmation()  # Or auto-continue for complex topics
```

---

## Key Takeaways

1. **Convergence threshold**: <15% improvement across 2 consecutive iterations triggers stop  
2. **Quality gates required**: All 5 must pass (or have documented exceptions) before final output  
3. **Maximum retries**: Try 3 different strategies before escalating  
4. **Escalation is valid**: Sometimes partial findings + specific guidance is the best answer  
