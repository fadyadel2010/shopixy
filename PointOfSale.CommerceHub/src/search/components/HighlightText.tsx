import { cn } from "@/utils";

type HighlightTextProps = {
  text: string;
  query: string;
  highlightedText?: string | null;
  className?: string;
  highlightClassName?: string;
};

const escapeRegex = (value: string) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

const getElasticHighlightParts = (value: string) => {
  const elasticHighlightRegex = /<em>(.*?)<\/em>/gi;
  const parts: Array<{ value: string; isHighlighted: boolean }> = [];
  let lastIndex = 0;

  for (const match of value.matchAll(elasticHighlightRegex)) {
    const matchedValue = match[0];
    const highlightedValue = match[1] ?? "";
    const matchIndex = match.index ?? 0;

    if (matchIndex > lastIndex) {
      parts.push({
        value: value.slice(lastIndex, matchIndex),
        isHighlighted: false
      });
    }

    if (highlightedValue.length > 0) {
      parts.push({
        value: highlightedValue,
        isHighlighted: true
      });
    }

    lastIndex = matchIndex + matchedValue.length;
  }

  if (lastIndex < value.length) {
    parts.push({
      value: value.slice(lastIndex),
      isHighlighted: false
    });
  }

  return parts.filter((part) => part.value.length > 0);
};

export const HighlightText = ({
  text,
  query,
  highlightedText,
  className,
  highlightClassName
}: HighlightTextProps) => {
  if (highlightedText && highlightedText.includes("<em>")) {
    const parts = getElasticHighlightParts(highlightedText);
    if (parts.length > 0) {
      return (
        <span className={className}>
          {parts.map((part, index) =>
            part.isHighlighted ? (
              <mark
                key={`${part.value}-${index}`}
                className={cn("rounded bg-amber-200/70 px-0.5 text-foreground", highlightClassName)}
              >
                {part.value}
              </mark>
            ) : (
              <span key={`${part.value}-${index}`}>{part.value}</span>
            )
          )}
        </span>
      );
    }
  }

  const terms = query
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .sort((left, right) => right.length - left.length);

  if (terms.length === 0) {
    return <span className={className}>{text}</span>;
  }

  const matcher = new RegExp(`(${terms.map(escapeRegex).join("|")})`, "ig");
  const parts = text.split(matcher).filter(Boolean);
  const normalizedTerms = new Set(terms.map((term) => term.toLowerCase()));

  return (
    <span className={className}>
      {parts.map((part, index) =>
        normalizedTerms.has(part.toLowerCase()) ? (
          <mark
            key={`${part}-${index}`}
            className={cn("rounded bg-amber-200/70 px-0.5 text-foreground", highlightClassName)}
          >
            {part}
          </mark>
        ) : (
          <span key={`${part}-${index}`}>{part}</span>
        )
      )}
    </span>
  );
};
