import type { CommentNode, CommentTreeNode } from '../core/api/models';

/**
 * Assembles flat thread nodes into the nested tree the view renders.
 *
 * The server sends threads flat and paged, because a popular thread is too large to send whole.
 * Nodes may arrive across several pages and from live updates, so this builds the tree from
 * `parentId` alone and orders siblings by creation time — it does not depend on the order the nodes
 * happened to arrive in. A node whose parent has not been loaded yet is simply not attached until
 * it has.
 */
export function buildThreadTree(
  nodes: readonly CommentNode[],
  rootId: string,
): CommentTreeNode | null {
  const children = new Map<string, CommentNode[]>();
  let root: CommentNode | undefined;

  for (const node of nodes) {
    if (node.id === rootId) {
      root = node;
      continue;
    }

    if (node.parentId) {
      const siblings = children.get(node.parentId) ?? [];
      siblings.push(node);
      children.set(node.parentId, siblings);
    }
  }

  if (!root) {
    return null;
  }

  const attach = (node: CommentNode): CommentTreeNode => {
    const replies = (children.get(node.id) ?? [])
      .slice()
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt))
      .map(attach);

    return { ...node, replies };
  };

  return attach(root);
}

/** Adds nodes that are not already present, keeping the first copy of each id. */
export function mergeNodes(
  existing: readonly CommentNode[],
  incoming: readonly CommentNode[],
): CommentNode[] {
  const seen = new Set(existing.map((node) => node.id));

  return [...existing, ...incoming.filter((node) => !seen.has(node.id))];
}
