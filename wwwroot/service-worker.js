const CACHE_NAME = 'lostandfound-v1';
const ASSETS_TO_CACHE = [
  '/',
  '/css/site.css',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
  '/images/icons/icon-192.png',
  '/images/icons/icon-512.png'
];

// Install: cache core assets
self.addEventListener('install', function(event) {
  event.waitUntil(
    caches.open(CACHE_NAME).then(function(cache) {
      console.log('[SW] Caching core assets');
      return cache.addAll(ASSETS_TO_CACHE).catch(function(err) {
        console.log('[SW] Cache error (non-critical):', err);
      });
    })
  );
  self.skipWaiting();
});

// Activate: clean old caches
self.addEventListener('activate', function(event) {
  event.waitUntil(
    caches.keys().then(function(keys) {
      return Promise.all(
        keys.filter(function(key) { return key !== CACHE_NAME; })
            .map(function(key) { return caches.delete(key); })
      );
    })
  );
  self.clients.claim();
});

// Fetch: network-first for navigation, cache-first for static
self.addEventListener('fetch', function(event) {
  var url = new URL(event.request.url);

  if (event.request.method !== 'GET') return;

  // For navigation requests: network-first, fallback to cache
  if (event.request.mode === 'navigate') {
    event.respondWith(
      fetch(event.request)
        .then(function(response) {
          var cloned = response.clone();
          caches.open(CACHE_NAME).then(function(cache) {
            cache.put(event.request, cloned);
          });
          return response;
        })
        .catch(function() {
          return caches.match(event.request).then(function(cached) {
            return cached || caches.match('/');
          });
        })
    );
    return;
  }

  // For static assets: cache-first
  event.respondWith(
    caches.match(event.request).then(function(cached) {
      return cached || fetch(event.request).then(function(response) {
        var cloned = response.clone();
        caches.open(CACHE_NAME).then(function(cache) {
          cache.put(event.request, cloned);
        });
        return response;
      });
    })
  );
});
