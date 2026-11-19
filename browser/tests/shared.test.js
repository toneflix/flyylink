import { test } from 'node:test';
import assert from 'node:assert/strict';
import '../src/shared/numbers.js';
import '../src/shared/page.js';
import '../src/shared/otp.js';

const { numbers, page, otp } = globalThis.FlyyLink;

test('finds phone numbers as people write them', () => {
  assert.equal(numbers.find('+234 803 123 4567'), '+2348031234567');
  assert.equal(numbers.find('(555) 010-9999'), '5550109999');
  assert.equal(numbers.find('tel:0803-123-4567'), '08031234567');
});

test('leaves things that are not phone numbers', () => {
  for (const text of ['2026-11-19', '19/11/2026', 'call me', '12', '1234', '+1 (555) 010-9999 ext', '1234567890123456']) {
    assert.equal(numbers.find(text), null, text);
  }
});

test('reads tel: and sms: links', () => {
  assert.deepEqual(numbers.fromLink('tel:+1-555-010-9999'), { scheme: 'tel', number: '+15550109999', body: '' });
  assert.deepEqual(numbers.fromLink('sms:+15550109999?body=Hi%20there'), { scheme: 'sms', number: '+15550109999', body: 'Hi there' });
  assert.equal(numbers.fromLink('mailto:someone@example.com'), null);
});

test('spaces numbers out', () => {
  assert.equal(numbers.pretty('+2348031234567'), '+234 803 123 4567');
  assert.equal(numbers.pretty('5550109'), '5550109');
});

test('names sites', () => {
  assert.equal(page.siteName('www.youtube.com'), 'YouTube');
  assert.equal(page.siteName('blog.example.org'), 'blog.example.org');
});

test('opens videos at the same second', () => {
  assert.equal(page.positionUrl('https://www.youtube.com/watch?v=abc', { seconds: 95.7 }), 'https://www.youtube.com/watch?v=abc&t=95s');
  assert.equal(page.positionUrl('https://vimeo.com/123', { seconds: 30 }), 'https://vimeo.com/123#t=30s');
  assert.equal(page.positionUrl('https://www.youtube.com/watch?v=abc', { seconds: 2 }), 'https://www.youtube.com/watch?v=abc');
});

test('opens pages at the same words', () => {
  const url = page.positionUrl('https://example.com/post#intro', { text: 'The quick brown fox jumps over the lazy dog again' });
  assert.equal(url, 'https://example.com/post#intro:~:text=The%20quick%20brown%20fox%20jumps%20over%20the%20lazy');
  assert.equal(page.positionUrl('https://example.com/', { text: 'Hi' }), 'https://example.com/');
});

test('knows what a TV can play', () => {
  assert.equal(page.mimeFor('https://cdn.example.com/a/movie.webm'), 'video/webm');
  assert.equal(page.mimeFor('https://cdn.example.com/stream'), 'video/mp4');
  assert.equal(page.mimeFor('https://cdn.example.com/song', true), 'audio/mpeg');
  assert.ok(page.isPlayableByTv('https://cdn.example.com/movie.mp4'));
  assert.ok(!page.isPlayableByTv('blob:https://example.com/123'));
});

test('names fetched files', () => {
  assert.equal(page.fileName('https://example.com/pics/My%20Photo.jpg'), 'My Photo.jpg');
  assert.equal(page.fileName('https://example.com/', 'image'), 'image');
});

test('scores code boxes', () => {
  assert.ok(otp.score({ type: 'text', autocomplete: 'one-time-code' }) >= 100);
  assert.ok(otp.score({ type: 'text', name: 'otp', inputMode: 'numeric', maxLength: 6 }) >= 50);
  assert.ok(otp.score({ type: 'text', id: 'verification-code' }) >= 50);
});

test('ignores boxes for other things', () => {
  assert.equal(otp.score({ type: 'password', autocomplete: 'one-time-code' }), 0);
  assert.equal(otp.score({ type: 'text', name: 'postcode' }), 0);
  assert.equal(otp.score({ type: 'text', name: 'promo_code' }), 0);
  assert.ok(otp.score({ type: 'text', name: 'first_name' }) < 50);
});
