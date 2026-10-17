---
title: Welcome to Markdownia
subtitle: A quiet place to write and read Markdown
author: The Markdownia Guide
---

Markdownia is a Markdown editor and viewer. Write on the left, read on the right, or collapse the editor and use it purely as a **reader** (press <kbd>Ctrl</kbd>+<kbd>\\</kbd>). This guide doubles as a tour of everything the preview can render.

## Text formatting

Use *italic*, **bold**, ***both***, ~~strikethrough~~, ++inserted text++, ==highlighted text==, and `inline code`. Pandoc-style superscript and subscript work too: E = mc^2^, H~2~O, and the 21^st^ century.

Smart punctuation turns "straight quotes" into curly ones, -- into an en dash, --- into an em dash, and ... into an ellipsis. You can switch it off on the **Design** tab.

Abbreviations such as HTML and GFM show their meaning when you hover over them in the preview.

*[HTML]: HyperText Markup Language
*[GFM]: GitHub Flavored Markdown

## Lists

- Bulleted lists use `-`, `*` or `+`
- They can be nested
    - like this
        - and this
- Press <kbd>Enter</kbd> to continue a list; <kbd>Tab</kbd> indents an item

1. Numbered lists count for you
2. Start anywhere you like
3. Pandoc's fancy lists are supported as well:

    a. alphabetic
    b. markers

- [x] Task lists are part of GitHub Flavored Markdown
- [x] Click **Task list** on the Home tab to make one
- [ ] Write something wonderful

## Quotes and callouts

> Typography exists to honor content.
>
> --- Robert Bringhurst

> [!NOTE]
> GitHub-style alerts render as callouts. Insert them from **Insert → Alert**.

> [!TIP]
> Change the reading font, size, line spacing and text width on the **Design** tab.

> [!WARNING]
> Unsaved changes are marked with a dot in the title bar.

::: important
Pandoc **fenced divs** with a class such as `note`, `tip`, `warning` or `important` become callouts as well.
:::

## Code

Fenced code blocks are highlighted for C#, C++, Python, JavaScript, TypeScript, Rust, Go, Java, SQL, shell and more.

```csharp
// A small record with a computed property
public sealed record Book(string Title, int Pages)
{
    public bool IsLong => Pages > 400;
}

var books = new List<Book> { new("Dune", 412), new("Hyperion", 482) };
Console.WriteLine($"{books.Count(b => b.IsLong)} long books");
```

```python
from dataclasses import dataclass

@dataclass
class Point:
    x: float
    y: float

    def norm(self) -> float:
        """Euclidean length."""
        return (self.x ** 2 + self.y ** 2) ** 0.5
```

```typescript
interface User { id: number; name: string }

async function load(id: number): Promise<User> {
  const res = await fetch(`/api/users/${id}`);
  return res.json() as Promise<User>;
}
```

## Mathematics

Inline math sits on the baseline: the roots of $ax^2 + bx + c = 0$ are given by the quadratic formula,

$$
x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}
$$

and Euler's identity $e^{i\pi} + 1 = 0$ ties five constants together. Display math supports sums, integrals and matrices:

$$
\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}
\qquad
\int_0^1 x^2\,dx = \frac{1}{3}
$$

## Tables

| Feature              | Syntax          | Origin   |
| :------------------- | :-------------: | -------: |
| Tables               | pipes           | GFM      |
| Footnotes            | `[^1]`          | Pandoc   |
| Definition lists     | `:` indent      | Pandoc   |
| Superscript          | `^text^`        | Pandoc   |
| Task lists           | `- [ ]`         | GFM      |

## Definition lists

Markdown
:   A lightweight markup language for writing formatted text in plain text.

Pandoc
:   A universal document converter, whose Markdown dialect adds footnotes, definition lists and more.

## Images

Insert a picture from the **Insert** tab, or drop an image file onto the editor. An image on its own line becomes a figure, with its alt text as the caption. Pictures with relative paths appear once the document has been saved.

To resize a picture, hover over it in the preview and drag the handle in its bottom-right corner. Markdownia records the width in Pandoc style, as in `![Chart](chart.png){width=480}`. Widths can also be given in other units, such as `{width=50%}`, `{width=3in}` or `{width=12em}`. Double-click the handle to restore the original size.

## Footnotes and links

Footnotes keep asides out of the way.[^aside] Click a footnote number to jump to it, and the arrow to come back.[^back]

Links look like [this one](https://commonmark.org), bare URLs like https://github.com are linked automatically, and you can link to headings in the same document, such as [Tables](#tables).

---

Happy writing.

[^aside]: Footnotes are numbered automatically, in the order they are referenced.

[^back]: Hover a footnote number to preview the note without leaving your place.
