# Promo graphics

Social media graphics for FlyyLink, with the post copy that goes with them in [POSTS.md](POSTS.md).

Each message (a theme in `page/promo.js`) is drawn in these sizes, in `images/`:

| Name                 | Size        | Where it goes                                       |
| -------------------- | ----------- | --------------------------------------------------- |
| `*-square`           | 1080 x 1080 | Instagram, Facebook and LinkedIn posts, WhatsApp    |
| `*-story`            | 1080 x 1920 | Instagram and Facebook stories, WhatsApp status, TikTok |
| `hero-portrait`      | 1080 x 1350 | Instagram and Facebook feed (takes up more room)    |
| `hero-link`          | 1200 x 630  | X, Facebook and LinkedIn link posts                 |
| `hero-x-header`      | 1500 x 500  | X profile header                                    |
| `hero-linkedin-cover`| 1584 x 396  | LinkedIn page cover                                 |

Stories keep the top and bottom 200 px clear for the apps' own bars. The banners leave room on the left for the profile photo.

The phone screens come from the store screenshots in `../android/page` (drawn plain, with no status bar or camera, so nothing looks like a particular brand's phone), the TV screens from `tv.js`, and the computer windows from `page/computer.js`. Images never say "Android" or show Google marks, so they can go anywhere, including the Amazon Appstore.

To regenerate, from `store/android` after `npm install` there:

```sh
npm run promo
```

To add a message, add a `Theme` to `THEMES` in `page/promo.js`. To add a size, add a `Format` to `FORMATS` and to the `PROMO` list.
