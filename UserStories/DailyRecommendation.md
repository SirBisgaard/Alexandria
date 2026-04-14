# Daily Recommendation

## Story

As a logged-in user of Alexandria,
Wants to see a daily recommendation of 1-2 books from my library that I have not read or archived.
So that I am able to get motivated to read more or remember forgotten books.

## Technical

On the index page, show 1-2 recommended books in the same style as the "Currently Reading" section.
The section title should be "Need something to read?".

The recommendation should be deterministic per day. 
Use the current date with no time component as a seed to pick the books.
This means every user sees the same recommendation for a given day, and refreshing the page does not change the picks.

Only books that meet all of the following criteria are eligible:
- Not archived
- Reading status is "ReadyToRead" or has no reading progress

If there are fewer than 2 eligible books, show however many are available.
If there are no eligible books, do not show the section.

## Acceptance Criteria

- [ ] Index page shows a "Need something to read?" section with 1-2 book recommendations
- [ ] Recommended books are not archived and have not been started
- [ ] The same books are shown for the entire day regardless of page refreshes
- [ ] A different day produces a different selection
- [ ] If no eligible books exist, the section is hidden
- [ ] The section uses the same visual style as "Currently Reading"

## Dependencies

The user is authenticated and logged into Alexandria.
The library contains at least one unread, non-archived book.
