export interface ParsedProductFaqItem {
    question: string;
    answer: string;
}

export interface ParsedProductNarrative {
    summary: string | null;
    overviewParagraphs: string[];
    detailParagraphs: string[];
    features: string[];
    faqItems: ParsedProductFaqItem[];
}

function normalizeText(value: string | null | undefined): string {
    return `${value ?? ''}`
        .replace(/\r\n/g, '\n')
        .replace(/\r/g, '\n')
        .trim();
}

function splitParagraphs(value: string): string[] {
    return value
        .split(/\n\s*\n/)
        .map((paragraph) => paragraph.trim())
        .filter(Boolean);
}

function extractSectionBlock(lines: string[], startIndex: number, endIndex: number): string {
    return lines
        .slice(startIndex, endIndex)
        .join('\n')
        .trim();
}

function stripBulletPrefix(value: string): string {
    return value.replace(/^[-*]\s*/, '').trim();
}

function parseFeatures(block: string): string[] {
    const features: string[] = [];

    block
        .split('\n')
        .map((line) => line.trim())
        .filter(Boolean)
        .forEach((line) => {
            const normalizedLine = stripBulletPrefix(line);
            if (normalizedLine) {
                features.push(normalizedLine);
            }
        });

    return features;
}

function parseFaqItems(block: string): ParsedProductFaqItem[] {
    const faqItems: ParsedProductFaqItem[] = [];
    const lines = block.split('\n');
    let currentQuestion = '';
    let currentAnswer = '';

    const commitCurrentItem = () => {
        const question = currentQuestion.trim();
        const answer = currentAnswer.trim();

        if (question || answer) {
            faqItems.push({
                question,
                answer,
            });
        }

        currentQuestion = '';
        currentAnswer = '';
    };

    lines.forEach((rawLine) => {
        const line = rawLine.trim();

        if (!line) {
            if (currentQuestion || currentAnswer) {
                commitCurrentItem();
            }
            return;
        }

        if (/^q\s*:/i.test(line)) {
            if (currentQuestion || currentAnswer) {
                commitCurrentItem();
            }

            currentQuestion = line.replace(/^q\s*:/i, '').trim();
            return;
        }

        if (/^a\s*:/i.test(line)) {
            currentAnswer = line.replace(/^a\s*:/i, '').trim();
            return;
        }

        const bulletQuestion = stripBulletPrefix(line);
        if (!currentQuestion && bulletQuestion !== line) {
            currentQuestion = bulletQuestion;
            return;
        }

        if (currentAnswer) {
            currentAnswer = `${currentAnswer} ${line}`.trim();
            return;
        }

        if (currentQuestion) {
            currentQuestion = `${currentQuestion} ${line}`.trim();
            return;
        }

        currentQuestion = line;
    });

    commitCurrentItem();

    return faqItems;
}

export function parseProductNarrative(value: string | null | undefined): ParsedProductNarrative {
    const normalizedValue = normalizeText(value);

    if (!normalizedValue) {
        return {
            summary: null,
            overviewParagraphs: [],
            detailParagraphs: [],
            features: [],
            faqItems: [],
        };
    }

    const lines = normalizedValue.split('\n');
    const featureIndex = lines.findIndex((line) => /^features\s*:\s*$/i.test(line.trim()));
    const faqIndex = lines.findIndex((line) => /^faq\s*:\s*$/i.test(line.trim()));
    const sectionStartIndices = [featureIndex, faqIndex]
        .filter((index) => index >= 0)
        .sort((left, right) => left - right);
    const overviewEndIndex = sectionStartIndices[0] ?? lines.length;
    const overviewBlock = extractSectionBlock(lines, 0, overviewEndIndex);
    const overviewParagraphs = splitParagraphs(overviewBlock);
    const summary = overviewParagraphs[0] ?? null;
    const detailParagraphs = overviewParagraphs.length > 1
        ? overviewParagraphs.slice(1)
        : overviewParagraphs;

    let features: string[] = [];
    let faqItems: ParsedProductFaqItem[] = [];

    if (featureIndex >= 0) {
        const featureEndIndex = faqIndex >= 0 && faqIndex > featureIndex ? faqIndex : lines.length;
        features = parseFeatures(extractSectionBlock(lines, featureIndex + 1, featureEndIndex));
    }

    if (faqIndex >= 0) {
        faqItems = parseFaqItems(extractSectionBlock(lines, faqIndex + 1, lines.length));
    }

    return {
        summary,
        overviewParagraphs,
        detailParagraphs,
        features,
        faqItems,
    };
}

export default parseProductNarrative;
