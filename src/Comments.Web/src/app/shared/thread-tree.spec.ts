import { describe, expect, it } from 'vitest';

import type { CommentNode } from '../core/api/models';
import { buildThreadTree, mergeNodes } from './thread-tree';

function node(id: string, parentId: string | null, createdAt: string, depth = 1): CommentNode {
  return {
    id,
    parentId,
    rootId: 'root',
    depth,
    author: { id: 'a', userName: 'Anonym', email: 'a@example.com' },
    textHtml: id,
    createdAt,
    attachments: [],
  };
}

describe('buildThreadTree', () => {
  it('nests replies under their parents', () => {
    const tree = buildThreadTree(
      [
        node('root', null, '2026-01-01T00:00:00Z'),
        node('a', 'root', '2026-01-01T00:01:00Z', 2),
        node('a1', 'a', '2026-01-01T00:02:00Z', 3),
      ],
      'root',
    );

    expect(tree?.replies.map((r) => r.id)).toEqual(['a']);
    expect(tree?.replies[0].replies.map((r) => r.id)).toEqual(['a1']);
  });

  it('orders siblings by creation time whatever order the pages arrived in', () => {
    const tree = buildThreadTree(
      [
        node('late', 'root', '2026-01-01T00:05:00Z', 2),
        node('root', null, '2026-01-01T00:00:00Z'),
        node('early', 'root', '2026-01-01T00:01:00Z', 2),
      ],
      'root',
    );

    expect(tree?.replies.map((r) => r.id)).toEqual(['early', 'late']);
  });

  it('holds back a node whose parent has not been loaded yet', () => {
    const tree = buildThreadTree(
      [
        node('root', null, '2026-01-01T00:00:00Z'),
        node('orphan', 'not-loaded', '2026-01-01T00:01:00Z', 3),
      ],
      'root',
    );

    expect(tree?.replies).toEqual([]);
  });

  it('returns null until the root itself is loaded', () => {
    expect(buildThreadTree([node('a', 'root', '2026-01-01T00:01:00Z', 2)], 'root')).toBeNull();
  });
});

describe('mergeNodes', () => {
  it('appends new nodes and ignores ones already present', () => {
    const first = node('a', null, '2026-01-01T00:00:00Z');
    const merged = mergeNodes(
      [first],
      [{ ...first, textHtml: 'changed' }, node('b', 'a', '2026-01-01T00:01:00Z', 2)],
    );

    expect(merged.map((n) => n.id)).toEqual(['a', 'b']);
    expect(merged[0].textHtml).toBe('a');
  });
});
