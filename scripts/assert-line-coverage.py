"""Fail when WasteBatteriesSubmitBackend line coverage is below the given percent.

Counts lines in the production assembly. Generated files under obj are excluded.
Types marked ExcludeFromCodeCoverage are already absent from the report.
"""

import sys
import xml.etree.ElementTree as ET


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def children(element: ET.Element, name: str) -> list[ET.Element]:
    return [child for child in element if local_name(child.tag) == name]


def line_coverage(path: str) -> tuple[int, int]:
    covered = 0
    total = 0
    for package in production_packages(ET.parse(path).getroot()):
        package_covered, package_total = package_line_coverage(package)
        covered += package_covered
        total += package_total

    return covered, total


def production_packages(root: ET.Element) -> list[ET.Element]:
    package_lists = children(root, "packages")
    if not package_lists:
        return []

    return [
        package
        for package in children(package_lists[0], "package")
        if package.get("name") == "WasteBatteriesSubmitBackend"
    ]


def package_line_coverage(package: ET.Element) -> tuple[int, int]:
    covered = 0
    total = 0
    for classes in children(package, "classes"):
        for cls in children(classes, "class"):
            class_covered, class_total = class_line_coverage(cls)
            covered += class_covered
            total += class_total

    return covered, total


def class_line_coverage(cls: ET.Element) -> tuple[int, int]:
    if is_generated(cls.get("filename")):
        return 0, 0

    covered = 0
    total = 0
    for line in coverage_lines(cls):
        total += 1
        if line_was_hit(line):
            covered += 1

    return covered, total


def coverage_lines(cls: ET.Element) -> list[ET.Element]:
    found: list[ET.Element] = []
    for lines in children(cls, "lines"):
        found.extend(children(lines, "line"))

    return found


def is_generated(filename: str | None) -> bool:
    normalized = (filename or "").replace("\\", "/")

    return "/obj/" in f"/{normalized}"


def line_was_hit(line: ET.Element) -> bool:
    hits = line.get("hits") or "0"

    return int(hits) > 0


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: assert-line-coverage.py <cobertura.xml> <threshold-percent>", file=sys.stderr)
        return 2

    path = sys.argv[1]
    threshold = float(sys.argv[2])
    covered, total = line_coverage(path)

    if total == 0:
        print("::error::No instrumented WasteBatteriesSubmitBackend lines were found in the coverage report.")
        return 1

    percent = 100 * covered / total
    print(f"Line coverage is {percent:.2f}% ({covered}/{total}). Threshold is {threshold:g}%.")

    if covered * 100 < threshold * total:
        print(f"::error::Line coverage {percent:.2f}% is below threshold {threshold:g}%.")
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
