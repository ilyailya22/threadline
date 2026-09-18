import { Pipe, SecurityContext, inject, type PipeTransform } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';

/**
 * Renders comment HTML that has already been sanitised by the server.
 *
 * This deliberately runs Angular's own sanitiser instead of `bypassSecurityTrustHtml`. The server
 * is the authority and its allowlist is far stricter than Angular's — so in normal operation this
 * pipe changes nothing. Its value is the day the server-side sanitiser has a bug: a payload that
 * slipped past it still has to get past Angular, and it will not.
 *
 * The one visible cost is that Angular strips `target="_blank"`, so links open in the same tab.
 * That is a cheap price for a second independent line of defence against stored XSS.
 */
@Pipe({ name: 'sanitizedHtml' })
export class SanitizedHtmlPipe implements PipeTransform {
  private readonly sanitizer = inject(DomSanitizer);

  transform(value: string | null | undefined): string {
    return this.sanitizer.sanitize(SecurityContext.HTML, value ?? '') ?? '';
  }
}
