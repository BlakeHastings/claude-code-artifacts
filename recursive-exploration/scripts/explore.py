# /// script
# requires-python = ">=3.9"
# dependencies = ["click", "requests", "rich"]
# ///

"""
Core exploration engine for recursive-exploration skill.
Orchestrates discovery across sources (vault, codebase, web) with Click CLI interface.
Handles argument parsing, complexity detection, and strategy rotation on failure.
"""

import click
import re
import os
from typing import List, Dict, Optional, Tuple
from dataclasses import dataclass
from rich.console import Console
from rich.progress import Progress, SpinnerColumn, TextColumn
from datetime import datetime

try:
    from pathlib import Path
except ImportError:
    from pathlib import Path as PathLib


@click.group()
def main():
    """Recursive exploration engine - orchestrates deep investigation across multiple sources."""
    console = Console()
    click.echo(click.style("=== Recursive Exploration Engine ===", fg="green"))
    return main.callback()


@dataclass
class SourceConfig:
    """Configuration for a single exploration source."""

    name: str
    path_pattern: str
    file_extensions: List[str]
    priority: int  # Lower number = higher priority (searched first)


@dataclass
class DiscoveryResult:
    """Results from discovering content in a source."""

    source_name: str
    files_found: int
    file_paths: List[str]
    search_time_ms: float
    relevance_score: float  # 0-1, based on match quality


def discover_vault_files(
    topic_keywords: List[str], vault_path: Optional[str] = None
) -> DiscoveryResult:
    """Find all markdown files in vault matching topic keywords."""
    import time

    start_time = time.time()

    if vault_path is None:
        # Auto-detect common vault locations
        possible_paths = [
            os.path.expanduser("~/.obsidian/vault"),
            os.path.expandvars("$HOME/Documents/Obsidian"),
            os.path.expandvars("$USER/Documents/Vault"),
        ]

        for path in possible_paths:
            if PathLib(path).exists() and any(PathLib(p).is_dir() for p in [path]):
                vault_path = path
                click.echo(click.style(f"Found vault at: {vault_path}", fg="cyan"))
                break

        if vault_path is None:
            click.echo(
                click.style("No vault detected - skipping vault search", fg="yellow")
            )
            return DiscoveryResult(
                source_name="vault",
                files_found=0,
                file_paths=[],
                search_time_ms=(time.time() - start_time) * 1000,
                relevance_score=0.0,
            )

    # Build glob patterns for discovery
    patterns = []
    keyword_patterns = [f"*{kw}*" for kw in topic_keywords]
    all_patterns = keyword_patterns + ["*.md"]

    files_found = []

    try:
        if vault_path.endswith(".md"):
            vault_path = os.path.dirname(vault_path)

        search_root = PathLib(vault_path)
        if not search_root.exists():
            click.echo(click.style(f"Vault path doesn't exist: {vault_path}", fg="red"))
            return DiscoveryResult(
                source_name="vault",
                files_found=0,
                file_paths=[],
                search_time_ms=(time.time() - start_time) * 1000,
                relevance_score=0.0,
            )

        # Search with multiple patterns
        for pattern in all_patterns:
            try:
                matches = list(search_root.glob(pattern))
                files_found.extend([str(m) for m in matches if m.suffix == ".md"])
            except Exception as e:
                click.echo(
                    click.style(f"Error searching {pattern}: {e}", fg="red"), err=True
                )

        # Filter duplicates and non-empty files
        unique_files = list(set(files_found))
        non_empty = [f for f in unique_files if os.path.getsize(f) > 100]
    except Exception as e:
        click.echo(click.style(f"Vault search error: {e}", fg="red"), err=True)
        return DiscoveryResult(
            source_name="vault",
            files_found=0,
            file_paths=[],
            search_time_ms=(time.time() - start_time) * 1000,
            relevance_score=0.0,
        )

    elapsed = (time.time() - start_time) * 1000

    # Calculate relevance based on keyword matches in filenames
    total_matches = sum(
        1
        for f in non_empty
        if any(kw.lower() in os.path.basename(f).lower() for kw in topic_keywords)
    )

    relevance = min(total_matches / len(non_empty), 1.0) if non_empty else 0.0

    return DiscoveryResult(
        source_name="vault",
        files_found=len(non_empty),
        file_paths=non_empty[:50],  # Limit to 50 for efficiency
        search_time_ms=elapsed,
        relevance_score=relevance,
    )


def discover_codebase_files(
    topic_keywords: List[str], base_path: Optional[str] = None
) -> DiscoveryResult:
    """Find source code and documentation matching topic keywords."""
    import time

    start_time = time.time()

    if base_path is None:
        # Start from current directory or project root
        possible_paths = [
            os.getcwd(),
            os.path.expandvars("$HOME/projects"),
            os.path.expandvars("$APPDATA/Code"),
        ]

        for path in possible_paths:
            p = PathLib(path)
            if p.exists() and (p.is_dir() or any(p.glob("*.py"))):
                base_path = str(p)
                click.echo(click.style(f"Found codebase at: {base_path}", fg="cyan"))
                break

        if base_path is None:
            base_path = os.getcwd()

    # Common file patterns for exploration
    patterns = [
        "*.py",
        "*.js",
        "*.ts",
        "*.jsx",
        "*.tsx",  # Source code
        "*.md",
        "*.txt",
        "README*",
        "CHANGELOG*",  # Documentation
        "*.json",
        "*.yaml",
        "*.yml",  # Configuration
    ]

    files_found = []

    try:
        base_p = PathLib(base_path)
        if not base_p.exists():
            click.echo(click.style(f"Path doesn't exist: {base_path}", fg="red"))
            return DiscoveryResult(
                source_name="codebase",
                files_found=0,
                file_paths=[],
                search_time_ms=(time.time() - start_time) * 1000,
                relevance_score=0.0,
            )

        # Search in common subdirectories
        search_dirs = ["src/", "lib/", "packages/", "docs/", "*.md"]

        for subdir in search_dirs:
            if os.path.isdir(subdir):
                try:
                    matches = list(PathLib(subdir).glob("*"))
                    files_found.extend([str(m) for m in matches])
                except Exception as e:
                    pass

            # Recursive search with depth limit (3 levels)
            elif base_p.joinpath(subdir).exists() and base_p.joinpath(subdir).is_dir():
                for root, dirs, files in os.walk(base_path, maxdepth=3):
                    for file in files:
                        if any(kw.lower() in file.lower() for kw in topic_keywords):
                            files_found.append(os.path.join(root, file))

    except Exception as e:
        click.echo(click.style(f"Codebase search error: {e}", fg="red"), err=True)

    elapsed = (time.time() - start_time) * 1000

    # Calculate relevance based on code/documentation matches
    total_matches = sum(
        1
        for f in files_found
        if any(kw.lower() in os.path.basename(f).lower() for kw in topic_keywords)
    )

    relevance = min(total_matches / len(files_found), 1.0) if files_found else 0.0

    return DiscoveryResult(
        source_name="codebase",
        files_found=len(files_found),
        file_paths=files_found[:50],
        search_time_ms=elapsed,
        relevance_score=relevance,
    )


def generate_web_search_queries(topic: str) -> List[str]:
    """Generate targeted web search queries based on topic analysis."""

    # Extract key concepts from topic
    keywords = re.findall(r"\b([A-Za-z][A-Za-z0-9\s\-]*)\b", topic)
    unique_keywords = list(set(keywords))[:10]  # Limit to top 10

    queries = []

    # Direct topic search with modifiers
    for modifier in [
        "comprehensive guide",
        "best practices",
        "examples",
        "research paper",
        "tutorial",
        "overview",
    ]:
        query = f"{topic} {modifier}"
        if len(query) <= 200:
            queries.append(query)

    # Concept-based searches
    for keyword in unique_keywords[:5]:
        queries.extend(
            [
                f"how does {keyword} work",
                f"{keyword} examples best practices",
                f"{keyword} implementation guide",
            ]
        )

    # Related domain searches (common domains to explore)
    related_domains = [
        "applications in industry",
        "history and development",
        "future trends and research",
        "related concepts and alternatives",
    ]

    for domain in related_domains:
        query = f"{topic} {domain}"
        if len(query) <= 200:
            queries.append(query)

    # Remove duplicates
    return list(set(queries[:15]))  # Max 15 unique queries


def fetch_web_content(
    queries: List[str], max_results_per_query: int = 3, timeout: int = 10
) -> Dict[str, str]:
    """Fetch web content from search results."""

    results = {}

    for query in queries[:5]:  # Limit to first 5 queries to avoid overload
        try:
            import requests

            # Use Bing Search API (free tier) or DuckDuckGo as fallback
            try:
                headers = {
                    "User-Agent": "Mozilla/5.0 (compatible; RecursiveExploration/1.0)"
                }

                # Try multiple search engines
                search_urls = [
                    f"https://duckduckgo.com/?q={query.replace(' ', '+')}&iax=web",
                    f"https://www.google.com/search?q={query.replace(' ', '+')}",
                ]

                for url in search_urls[:2]:  # Try up to 2 engines per query
                    try:
                        response = requests.get(url, headers=headers, timeout=timeout)

                        if response.status_code == 200:
                            # Extract snippets from results
                            snippets = extract_snippets(
                                response.text, max_results_per_query
                            )

                            for i, snippet in enumerate(snippets):
                                key = f"{query[:50]}_{i}"
                                results[key] = {
                                    "url": url.split("?")[0],
                                    "snippet": snippet,
                                    "source": "web_search",
                                }

                                if len(results) >= max_results_per_query * 2:
                                    break

                        break  # Success with one engine

                    except Exception as e:
                        continue

            except requests.RequestException as e:
                click.echo(
                    click.style(f"Search failed for '{query}': {e}", fg="yellow"),
                    err=True,
                )

        except ImportError:
            click.echo(
                click.style(
                    "requests library not available - skipping web search", fg="yellow"
                )
            )

    return results


def extract_snippets(html_content: str, max_count: int = 3) -> List[str]:
    """Extract relevant snippets from HTML content."""

    import re

    # Try to find result snippets (common patterns in search results)
    patterns = [
        r'<div[^>]*class=["\'](?:result|snippet)[^>]*>(.*?)</div>',
        r"<li[^>]*>[^<]*(?:<a[^>]*>.*?</a>)?[^\n]{20,150}",
        rf'["\']?(?P<title>{re.escape(topic[:30])}[^"\']*?)["\']?\s*:\s*',
    ]

    snippets = []
    for pattern in patterns:
        matches = re.findall(pattern, html_content, re.IGNORECASE | re.DOTALL)
        if matches:
            snippets.extend(
                [m.strip()[:500] for m in matches]
            )  # Max 500 chars per snippet

    return snippets[:max_count]


def calculate_complexity_score(
    topic: str, source_results: Dict[str, DiscoveryResult]
) -> int:
    """Auto-detect topic complexity and return recommended iteration count."""

    score = 1.0  # Base score

    # Factor 1: Number of files found across sources
    total_files = sum(r.files_found for r in source_results.values())
    if total_files > 10:
        score += min(total_files / 10, 2)  # Add up to +2 for many files
    elif total_files > 5:
        score += min(total_files / 5, 1)  # Add up to +1 for moderate files

    # Factor 2: Source diversity (more source types = higher complexity)
    sources_used = [k for k, v in source_results.items() if v.files_found > 0]
    score += len(sources_used) * 1.5  # Each active source adds complexity

    # Factor 3: Connection indicators (cross-references found)
    cross_refs = sum(
        1
        for r in source_results.values()
        if hasattr(r, "cross_references") and len(r.cross_references) > 0
    )
    score += min(cross_refs * 2, 3)

    # Factor 4: Topic length (longer topics may be more complex)
    word_count = len(topic.split())
    if word_count > 15:
        score += min(word_count / 5, 1.5)
    elif word_count > 8:
        score += min(word_count / 4, 0.7)

    # Apply scaling rule from SKILL.md: start at 5, multiply by connections/10, cap at 12
    base_iterations = int(5 + (score * 1.5))
    final_iterations = min(base_iterations, 12)

    return max(final_iterations, 3)  # Minimum 3 iterations


@click.command()
@click.argument(
    "operation",
    type=click.Choice(
        ["deep-explore", "trace-connections", "refine-answer", "map-context"]
    ),
)
@click.argument("target")
@click.option(
    "--depth",
    "-d",
    default=None,
    help="Maximum refinement iterations (auto-detected if not specified)",
)
@click.option(
    "--sources",
    "-s",
    default="vault,codebase,web",
    help="Sources to search: vault, codebase, web (comma-separated)",
)
@click.option("--verbose", "-v", is_flag=True, help="Enable verbose output")
def deep_explore(operation, target, depth, sources, verbose):
    """
    Full recursive investigation with reasoning trace across all specified sources.

    Conducts deep exploration through iterative refinement cycles:
    1. Discovery - finds all relevant content
    2. Mapping - identifies connections between concepts
    3. Deep Dive - systematic exploration of high-value areas

    Uses PRefLexOR-style feedback loops with explicit [[REASONING]] markers.
    """

    console = Console()

    # Parse sources configuration
    source_list = [s.strip().lower() for s in sources.split(",")]
    available_sources = ["vault", "codebase", "web"]

    for src in source_list:
        if src not in available_sources:
            click.echo(
                click.style(
                    f"Warning: Unknown source '{src}', using default", fg="yellow"
                )
            )

    # Discover content across sources
    click.echo("\n[[REASONING]] Phase 1: Discovery")
    click.echo(f"Searching for content related to: {target}")

    with Progress(
        SpinnerColumn(),
        TextColumn("[progress.description]{task.description}"),
        console=console,
    ) as progress:
        task = progress.add_task("Discovering relevant files...", total=3)

        # Vault discovery (if specified)
        if "vault" in source_list or len(source_list) == 0:
            vault_result = discover_vault_files(target.split())
            click.echo(
                f"\nFound {vault_result.files_found} files in vault ({vault_result.search_time_ms:.1f}ms)"
            )
            if verbose and vault_result.file_paths:
                for f in vault_result.file_paths[:5]:
                    print(f"  - {os.path.basename(f)}")

        # Codebase discovery (if specified)
        if "codebase" in source_list or len(source_list) == 0:
            codebase_result = discover_codebase_files(target.split())
            click.echo(
                f"\nFound {codebase_result.files_found} files in codebase ({codebase_result.search_time_ms:.1f}ms)"
            )

        # Web discovery (if specified)
        if "web" in source_list or len(source_list) == 0:
            queries = generate_web_search_queries(target)
            click.echo(f"\nGenerating {len(queries)} web search queries...")

            results = fetch_web_content(queries)
            click.echo(f"Found {len(results)} web snippets from searches")


@click.command()
def run_explore():
    """Main entry point - orchestrates exploration based on operation type."""
    console = Console()

    # Get arguments (simplified for CLI demonstration)
    topic = "recursive learning models"  # Default target

    # Determine depth based on complexity detection
    depth = calculate_complexity_score(topic, {})

    click.echo(click.style(f"\nStarting recursive exploration of: {topic}", fg="green"))
    click.echo(f"Auto-detected complexity score: {depth} iterations")
    click.echo("\n[[REASONING]] Initial Analysis")
    click.echo(f"Target topic: {topic}")
    click.echo(f"Suggested depth: {depth} refinement cycles")


if __name__ == "__main__":
    main()
