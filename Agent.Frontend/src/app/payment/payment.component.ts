import { Component, AfterViewInit } from '@angular/core';

declare var Moyasar: any;

@Component({
  selector: 'app-payment',
  standalone: true, // ✅ مهم جدًا عشان يشتغل في نظام بدون Module
  templateUrl: './payment.component.html',
  styleUrls: ['./payment.component.css']
})
export class PaymentComponent implements AfterViewInit {
  ngAfterViewInit(): void {
    setTimeout(() => {
      if (typeof Moyasar !== 'undefined') {
        Moyasar.init({
          element: '.mysr-form',
          amount: 1000,
          currency: 'SAR',
          description: 'Coffee Order #1',
          publishable_api_key: 'pk_test_mwxvM7LuU1coAmTgigjErrbs5YvAaZFveCR9J4f4',
          callback_url: 'https://moyasar.com/thanks',
          supported_networks: ['visa', 'mastercard', 'mada'],
          methods: ['creditcard']
        });
      } else {
        console.error('⚠️ Moyasar script not loaded.');
      }
    }, 500);
  }
}
