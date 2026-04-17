# Context Traversal Methodology

Systematic approach to exploring context space across multiple sources (vault, codebase, internet) using three-phase discovery methodology.

---

## Three-Phase Exploration Approach

### Phase 1: Discovery - Finding All Relevant Content

Goal: Exhaustively collect all content that could be relevant without premature filtering.

#### Vault File Discovery Pattern
```python
def discover_vault_files(topic_keywords):
    """Find all markdown files in vault matching topic"""
    
    # Broad initial search using multiple patterns
    patterns = [
        f"*{topic_keyword}*",     # Direct matches
        f"*.md",                   # All markdown for manual filtering
        "*related*",               # Related terminology  
        "*connected*",             # Cross-references
    ]
    
    found_files = []
    for pattern in patterns:
        files = glob.glob(f"${VOLD_PATH}/{pattern}")
        found_files.extend(files)
    
    # Remove duplicates and filter empty files
    unique_files = list(set(found_files))
    return [f for f in unique_files if os.path.getsize(f) > 0]

# Usage example
vault_files = discover_vault_files(["recursive", "learning"])
print(f"Found {len(vault_files)} relevant markdown files")
```

#### Codebase Discovery Pattern
```python
def discover_codebase_relevant_files(topic_keywords):
    """Find source code and documentation matching topic"""
    
    # Search in common locations
    search_paths = [
        "src/",
        "lib/",
        "packages/",
        "docs/",
        "*.md",  # Documentation files
        "README*"
    ]
    
    found_files = []
    for path in search_paths:
        if os.path.exists(path):
            files = glob.glob(f"{path}/*")
            found_files.extend(files)
        
        # Recursive search with depth limit
        if os.path.isdir(path):
            for root, dirs, files in os.walk(path, maxdepth=3):
                for file in files:
                    if any(keyword.lower() in file.lower() for keyword in topic_keywords):
                        found_files.append(os.path.join(root, file))
    
    return found_files
```

#### Web Search Query Generation
```python
def generate_web_search_queries(topic, context_sources):
    """Generate targeted queries based on local findings"""
    
    # Extract key concepts from discovered content
    concepts = extract_key_concepts(context_sources)
    
    # Generate query variations
    queries = []
    
    # Direct topic search
    queries.append(f"{topic} comprehensive guide")
    
    # Concept-based searches  
    for concept in concepts[:5]:  # Top 5 most relevant
        queries.append(f"how does {concept} work")
        queries.append(f"{concept} examples best practices")
    
    # Related domain searches
    domains = find_related_domains(topic)
    for domain in domains:
        queries.append(f"{topic} applications in {domain}")
    
    return queries[:10]  # Limit to prevent overload

# Example query generation
queries = generate_web_search_queries("recursive learning models", [found_files])
```

---

### Phase 2: Mapping - Identifying Connections Between Concepts

Goal: Build relationship graph from discovered content to understand how concepts interlink.

#### Cross-Reference Detection (Deterministic)

Pattern matching for explicit cross-references:

```python
def find_cross_references(content_list):
    """Detect links and references between documents"""
    
    all_refs = []
    
    for doc in content_list:
        with open(doc, 'r', encoding='utf-8') as f:
            text = f.read()
            
        # Find markdown links [[ ]] 
        md_links = re.findall(r'\[\[(.*?)\]\]', text)
        
        # Find internal links [text](path)
        wiki_links = re.findall(r'\[([^\]]+)\]\(([^)]+)\)', text)
        
        # Find "see also" references
        see_also = re.findall(r'see\s+also[:]\s+(.*?)(?:\n\n|\n|$)', text, re.IGNORECASE)
        
        all_refs.extend({
            'source': doc,
            'type': 'markdown' if md_links else ('wiki' if wiki_links else 'text'),
            'references': md_links + [link for _, link in wiki_links] + see_also
        })
    
    return all_refs

# Build connection graph from cross-references
connections = find_cross_references(vault_files)
```

#### Conceptual Link Inference (LLM-based)

Semantic relationships between concepts:

```python
def infer_conceptual_links(content_summary, topic):
    """Use LLM to identify conceptual connections beyond explicit references"""
    
    prompt = f"""Based on the following content about {topic}, identify relationships 
between key concepts. Look for implicit connections that aren't explicitly documented.

Content Summary:
{content_summary}

Identify:
1. Concepts that are related but not explicitly linked in documents
2. Common themes across different files
3. Gaps where multiple sources should be connected
4. Contradictions between different interpretations

Output as JSON with concept_id, description, and relationships array."""
    
    response = generate(prompt)
    return parse_json_from_response(response)

# Example usage after Phase 1 discovery
summary = summarize_discovered_content([doc for doc in vault_files])
conceptual_links = infer_conceptual_links(summary, "recursive learning")
```

#### Building the Knowledge Graph

Combining deterministic and LLM-based connections:

```python
class ContextGraph:
    def __init__(self):
        self.nodes = {}  # concept_id -> {name, type, sources}
        self.edges = []  # [(source_node, target_node, relationship_type)]
    
    def add_source_content(self, filepath):
        """Add content from a file as nodes"""
        
        with open(filepath) as f:
            text = f.read()
        
        # Extract concept mentions
        concepts = extract_concepts(text)
        
        for concept in concepts:
            if concept not in self.nodes:
                self.nodes[concept] = {
                    'name': concept,
                    'type': 'concept',  # Could also be 'source' or 'pattern'
                    'sources': [],
                    'confidence': 0.5  # Initial confidence score
                }
            elif filepath not in self.nodes[concept]['sources']:
                self.nodes[concept]['sources'].append(filepath)
    
    def add_explicit_edge(self, source_id, target_id, relationship):
        """Add explicitly documented relationship"""
        
        edge = (source_id, target_id, relationship)
        if edge not in self.edges:
            self.edges.append(edge)
    
    def infer_relationships(self, content_files):
        """Use LLM to add implicit relationships"""
        
        # Generate graph inference prompt
        nodes_list = [self.nodes[k]['name'] for k in self.nodes]
        
        prompt = f"""Given these concepts: {', '.join(nodes_list)}

Based on analysis of: {content_files}

Identify relationships that should exist between concepts but aren't explicitly documented.
Format as list of tuples: (source, target, relationship_type, confidence)"""
        
        response = generate(prompt)
        edges = parse_relationships(response)  # Custom parser
        
        for edge in edges:
            if all(n in self.nodes for n in [edge[0], edge[1]]):
                self.edges.append((edge[0], edge[1], edge[2]))
    
    def get_connected_components(self):
        """Find clusters of interconnected concepts"""
        
        # Build adjacency list
        adj = {node: [] for node in self.nodes}
        for src, tgt, _ in self.edges:
            adj[src].append(tgt)
        
        # Find connected components using BFS
        visited = set()
        components = []
        
        for start_node in self.nodes:
            if start_node not in visited:
                component = [start_node]
                queue = [start_node]
                
                while queue:
                    node = queue.pop(0)
                    if node not in visited:
                        visited.add(node)
                        for neighbor in adj.get(node, []):
                            if neighbor not in visited:
                                component.append(neighbor)
                                queue.append(neighbor)
                
                components.append(component)
        
        return sorted(components, key=len, reverse=True)

# Build graph from exploration results
graph = ContextGraph()
for doc in vault_files + codebase_files:
    graph.add_source_content(doc)

graph.infer_relationships([doc for doc in vault_files])
components = graph.get_connected_components()

print(f"Found {len(components)} connected components")
for i, comp in enumerate(components[:3], 1):
    print(f"\nComponent {i} ({len(comp)} concepts): {comp}")
```

---

### Phase 3: Deep Dive - Systematic Exploration of High-Value Areas

Goal: Selective exploration focusing on most valuable areas identified through mapping.

#### Priority Scoring for Topics

Deterministic scoring based on multiple factors:

```python
def calculate_topic_priority(topic, content_sources):
    """Score topics by multiple priority factors"""
    
    scores = {
        'source_diversity': 0,     # How many different sources mention this
        'connection_count': 0,     # Number of related concepts linked
        'depth_indicators': 0,     # Signs of deep treatment (examples, code)
        'ambiguity_score': 1.0,    # Starts high for vague topics
        'user_priority': 0         # Manual override weight
    }
    
    for source in content_sources:
        with open(source) as f:
            text = f.read()
        
        # Count mentions across sources (diversity)
        scores['source_diversity'] += 1
        
        # Check for depth indicators
        if re.search(r'\bexample[s]?\s*[:]\s+', text, re.IGNORECASE):
            scores['depth_indicators'] += 0.2
        if re.search(r'\bcode[:]\s*', text, re.IGNORECASE):
            scores['depth_indicators'] += 0.3
        
        # Check for complexity signals (longer treatment = higher score)
        topic_mentions = len(re.findall(f'\*\s*{topic}\s*\*', text))
        if topic_mentions > 2:
            scores['connection_count'] += topic_mentions
    
    # Normalize and combine
    total_score = sum(scores.values()) * 0.15 + (scores['source_diversity'] * 0.3)
    
    return {topic: total_score}

# Calculate priorities for all discovered topics
all_topics = extract_all_unique_topics([doc for doc in vault_files])
priorities = calculate_topic_priority(all_topics, [vault_files[0]])
```

#### Traversal Strategy Selection Guide

Choosing the right exploration strategy based on context characteristics:

| Strategy | Use When... | Characteristics | Max Depth |
|----------|-------------|-----------------|-----------|
| **Breadth-first** | Early discovery phase, broad topic coverage needed | Wide shallow exploration, many connections at each level | 2 levels |
| **Depth-first** | Narrow deep dive, focused question | Single thread to completion before branching | Unlimited (until convergence) |
| **Hybrid** | Most general cases, balanced approach | Explores breadth then dives into high-value areas | Variable based on topic |
| **Targeted** | Specific answer needed quickly | Focus only on relevant sub-areas, skip unrelated content | 1-2 levels |

#### Strategy Implementation Pattern

```python
class ExplorationStrategy:
    def __init__(self, traversal_type='hybrid', max_depth=5):
        self.type = traversal_type
        self.max_depth = max_depth
    
    def select_next_topic(self, current_context, priority_scores, visited):
        """Select next exploration target based on strategy"""
        
        available_topics = [t for t in priority_scores 
                          if t not in visited and priority_scores[t] > 0.3]
        
        if not available_topics:
            return None
        
        # Strategy-specific selection logic
        if self.type == 'breadth-first':
            # Always pick highest-scoring unvisited topic
            return max(available_topics, key=lambda t: priority_scores[t])
            
        elif self.type == 'depth-first':
            # Pick child of current context (if exists) or best remaining
            children = find_children(current_context, available_topics)
            if children:
                return max(children, key=lambda t: priority_scores[t])
            else:
                return max(available_topics, key=lambda t: priority_scores[t])
                
        elif self.type == 'targeted':
            # Only consider topics directly related to original question
            relevance = calculate_relevance(t, original_question) for t in available_topics
            return argmax(relevance)
        
        else:  # hybrid (default)
            # Balance between breadth and depth
            score = priority_scores.get(available_topics[0], 0) * 0.5 + \
                    min(len(visited), self.max_depth - 1) / max_depth * 0.5
            return argmax(score, available_topics)

# Strategy selection based on current phase
strategy = ExplorationStrategy(traversal_type='hybrid', max_depth=4)
next_topic = strategy.select_next_topic(current_context, priorities, visited_topics)
```

---

## Source Integration Patterns

### Vault + Codebase Combined Search

```python
def integrated_vault_codebase_search(topic):
    """Search both vault and codebase simultaneously"""
    
    # Phase 1: Find all potentially relevant files
    vault_matches = [f for f in glob.glob("${VAULT_PATH}/*{topic}*.{md}") 
                    if os.path.getsize(f) > 50]  # Skip empty
    
    codebase_matches = find_codebase_files(topic, ["*.py", "*.js", "*.ts", "README*"])
    
    # Phase 2: Cross-reference to identify connections
    cross_refs = []
    for vault_file in vault_matches[:10]:  # Limit for efficiency
        with open(vault_file) as f:
            vault_text = f.read()
        
        for code_file in codebase_matches[:5]:
            try:
                with open(code_file) as cf:
                    code_text = cf.read()
                
                # Check if they reference each other
                if re.search(r'\[\[\s*' + os.path.basename(vault_file) + r'\s*\]\]', code_text):
                    cross_refs.append((vault_file, code_file))
            except:
                continue
    
    return {
        'vault_files': vault_matches,
        'codebase_files': codebase_matches, 
        'cross_references': cross_refs
    }

# Example usage  
search_results = integrated_vault_codebase_search("machine learning")
print(f"Found {len(search_results['vault_files'])} vault files")
```

### Web + Local Content Synthesis

Combining internet research with local findings:

```python
def synthesize_web_local_findings(topic, local_content, web_results):
    """Integrate web sources with locally discovered content"""
    
    # Extract key claims from local content  
    local_claims = extract_claims(local_content)
    
    # Verify/expand against web results
    verified_claims = []
    
    for claim in local_claims:
        matching_web_results = [r for r in web_results 
                               if keyword_match(claim, r['snippet'])]
        
        if len(matching_web_results) > 0:
            # Verified by multiple sources
            verified_claims.append({
                'claim': claim,
                'confidence': min(len(matching_web_results), 3) / 3.0,
                'sources': [r['url'] for r in matching_web_results[:2]]
            })
        else:
            # Unverified - flag for further investigation
            verified_claims.append({
                'claim': claim,
                'confidence': 0.5,
                'status': 'unverified',
                'suggestion': f'Search web for evidence of: {claim}'
            })
    
    return verified_claims

# Example integration
local = [vault_files[0], vault_files[1]]
web_results = search_multiple_queries(generate_web_search_queries("topic", local))
synthesized = synthesize_web_local_findings("topic", local, web_results)
```

---

## Convergence in Phase 3

Deep Dive phase stops when convergence criteria met:

```python
def check_deep_dive_convergence(current_understanding, previous_iteration):
    """Determine if deep dive has converged"""
    
    # Multiple convergence signals:
    
    # 1. No new high-priority topics discovered in last 2 iterations
    new_topics = find_new_relevant_content(current_understanding)
    if len(new_topics) < 3 and iteration > max_iterations - 2:
        return True, "No significant new content found"
    
    # 2. Understanding quality score plateaued
    current_score = calculate_quality_score(current_understanding)
    prev_score = calculate_quality_score(previous_iteration)
    
    if abs(current_score - prev_score) < 0.15:  # Less than 15% improvement
        return True, "Understanding quality plateaued"
    
    # 3. Maximum depth reached and all high-value areas explored
    if iteration >= max_depth and all_high_priority_explored(current_understanding):
        return True, "Maximum exploration depth reached"
    
    return False, None

# Convergence check in main loop
if converged, reason = check_deep_dive_convergence(current_state, previous_state):
    log_convergence(reason)
    break  # Exit refinement loop
```

---

## Key Takeaways

1. **Three-phase approach**: Discovery (collect all) → Mapping (find connections) → Deep Dive (focus on high-value)
2. **Hybrid deterministic+LLM**: Pattern matching for explicit refs, semantic inference for conceptual links  
3. **Strategy selection matters**: Choose traversal method based on exploration phase and topic characteristics
4. **Convergence detection essential**: Prevent infinite loops with multiple stopping criteria
