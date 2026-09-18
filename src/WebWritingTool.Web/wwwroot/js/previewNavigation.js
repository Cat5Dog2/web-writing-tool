export function headings(container) {
    return [...container.querySelectorAll("h2, h3")].map((heading, index) => {
        // IDs are generated locally. Only sanitized, already displayed heading text is read.
        const id = `preview-section-${index + 1}`;
        heading.id = id;
        return { id, title: heading.textContent, level: Number(heading.tagName.slice(1)) };
    });
}
