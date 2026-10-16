# Privacy policy

*Last updated: 16 October 2026*

Expense Tracker is a personal, non-commercial project by Matas Dzingulevičius
(github.com/matas108). This page explains what the app stores and why.

## What the app stores

- **Your account:** your email address and a hashed password (the password itself is never stored).
- **Your finance data:** the accounts, categories and transactions you create, import from CSV
  files, or sync from your bank.
- **Bank connections:** which bank you connected, the provider's session reference, and when
  access ends.

## Bank access

Bank sync goes through [Enable Banking](https://enablebanking.com), an open-banking (PSD2)
provider. You sign in on your bank's own page; the app never sees or stores your bank login.
Access is **read-only** (balances and transactions), it can't make payments, and it ends
automatically when the consent period expires or when you disconnect the bank in the app.

## Where it's stored

On Microsoft Azure (App Service and Azure SQL Database) in the Sweden Central region, in the EU.
Data is sent over HTTPS only.

## Sharing

Your data is not sold, shared or used for advertising. It is only processed to show you your
own finances.

## Your choices

- Disconnect a bank at any time on the **Bank** page. Transactions already synced stay until you
  delete them.
- To have your account and all its data deleted, or to ask what is stored about you, open an
  issue at https://github.com/matas108/ExpenseTracker/issues and you'll be contacted privately.
